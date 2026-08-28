import { DestroyRef, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { RealtimeService } from '../../core/realtime.service';
import { syncOn } from '../../core/realtime-sync';
import { Perm } from '../../core/permissions';
import {
  PagedResult, Priority, StatusCountDto, VerificationResult, VerificationStatus,
  VerificationSummaryDto,
} from '../../core/models';
import { sinceLabel } from '../../core/format';
import { priorityLabel, verificationResultLabel, verificationStatusLabel } from '../../core/labels';
import { ChipComponent, PageHeaderComponent } from '../../shared/ui';
import { ViewTabsComponent } from '../../shared/view-tabs.component';
import { StatusTilesComponent } from '../../shared/status-tiles.component';
import { SortState } from '../../shared/sort-header.component';
import { columnFilters } from '../../shared/column-filter.component';
import { DataGridComponent, GridCellDirective, GridColumn } from '../../shared/data-grid.component';
import { VerificationCreateDialog } from './verification-create-dialog.component';
import { VerificationAssignDialog } from './verification-assign-dialog.component';

const PRIORITIES: Priority[] = ['Critical', 'High', 'Normal', 'Low'];

const STATUSES: VerificationStatus[] = [
  'Requested', 'Assigned', 'InProgress', 'Completed', 'Cancelled',
];

const RESULTS: VerificationResult[] = [
  'IssueConfirmed', 'WorkingCorrectly', 'ConfigurationOrDataIssue', 'NeedsClarification',
  'Inconclusive',
];

/** By urgency, not alphabetically: Critical must sort above High. */
const PRIORITY_ORDER: Record<Priority, number> = { Critical: 0, High: 1, Normal: 2, Low: 3 };

/** By where it has got to, so a queue reads in the order it moves through. */
const STATUS_ORDER: Record<VerificationStatus, number> = {
  Requested: 0, Assigned: 1, InProgress: 2, Completed: 3, Cancelled: 4,
};

/**
 * Checks: assigned investigation into whether something is actually broken.
 *
 * The one departure from the paged grids is `mode="local"` — filtering and sorting happen in the
 * grid rather than on the server, and that is correct for the same reason it is on the daily
 * report: the whole set arrives in one call, so narrowing it locally cannot misreport a total. A
 * check is only ever raised for a request somebody could not decide, so this is tens of rows.
 */
@Component({
  selector: 'app-verification-list',
  standalone: true,
  imports: [
    DatePipe, RouterLink, MatButtonModule, MatIconModule, PageHeaderComponent,
    ChipComponent, StatusTilesComponent, DataGridComponent, GridCellDirective, ViewTabsComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Checks">
        @if (canCreate()) {
          <button matButton="filled" (click)="raise()">
            <mat-icon>add</mat-icon> Raise a check
          </button>
        }
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon> Refresh</button>
      </app-page-header>

      <app-view-tabs group="quality" />

      <app-status-tiles
        [counts]="tiles()"
        [selected]="view()"
        [total]="all().length"
        (pick)="setView($event)" />

      <app-data-grid
        mode="local"
        [rows]="inView()" [columns]="columns"
        [loading]="loading()"
        [filters]="filters" [externalFilter]="view() !== null"
        [sort]="sort()" (sortChange)="sort.set($event)"
        emptyMessage="Nothing has been sent for checking" emptyIcon="fact_check"
        noMatchesMessage="No checks match those filters."
        [clickable]="true" (rowClick)="open($event)" (clearedFilters)="view.set(null)">

        <ng-template gridCell="number" let-v>
          <a class="grid-link mono" [routerLink]="['/verifications', v.id]">
            {{ v.verificationNumber }}
          </a>
        </ng-template>

        <ng-template gridCell="target" let-v>
          <a class="grid-title" [routerLink]="['/verifications', v.id]">{{ v.title }}</a>
          @if (v.targetSummary && v.targetSummary !== v.title) {
            <div class="muted small truncate">{{ v.targetSummary }}</div>
          }
        </ng-template>

        <ng-template gridCell="priority" let-v>
          <app-chip [value]="v.priority" kind="priority" />
        </ng-template>

        <ng-template gridCell="status" let-v>
          <app-chip [value]="v.status" kind="verificationStatus" />
        </ng-template>

        <!--
          A name, not a dropdown of people — the same rule the task grid follows. "-" means nobody
          has it, which is the one answer with no name to type and the one worth looking for most.
        -->
        <ng-template gridCell="checker" let-v>
          @if (v.assignedToDisplayName) {
            <span class="truncate">{{ v.assignedToDisplayName }}</span>
          } @else { <span class="muted">—</span> }
        </ng-template>

        <ng-template gridCell="raised" let-v>
          <span [title]="v.requestedAt | date: 'MMM d, y HH:mm'">{{ since(v.requestedAt) }}</span>
        </ng-template>

        <ng-template gridCell="outcome" let-v>
          @if (v.result) {
            <app-chip [value]="v.result" kind="verificationResult" />
          } @else { <span class="muted">—</span> }
        </ng-template>

        <ng-template gridCell="action" let-v>
          @if (v.status === 'Requested' && canWork()) {
            <button class="grid-action" (click)="claim(v); $event.stopPropagation()">Take it</button>
          } @else if (v.status === 'Requested' && canCreate()) {
            <button class="grid-action" (click)="assign(v); $event.stopPropagation()">Assign</button>
          } @else if (v.assignedToUserId === myId() && !isFinished(v)) {
            <a class="grid-action" [routerLink]="['/verifications', v.id]"
               (click)="$event.stopPropagation()">Open</a>
          }
        </ng-template>
      </app-data-grid>
    </div>
  `,
  styles: `
    .truncate { display: inline-block; max-width: 240px; }
  `,
})
export class VerificationListComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly dialog = inject(MatDialog);
  private readonly realtime = inject(RealtimeService);

  readonly loading = signal(true);
  readonly sort = signal<SortState>({ by: null, descending: true });

  /** The status tile that is lit, or null for all. */
  readonly view = signal<VerificationStatus | null>(null);

  /** Every check the caller may see. The tiles and the list are both counted from this. */
  readonly all = signal<VerificationSummaryDto[]>([]);

  /**
   * Nothing to re-fetch: the rows are already here, so the filter row only has to invalidate the
   * grid's computed. Passing a no-op keeps the debounce-on-typing behaviour every other grid has.
   */
  readonly filters = columnFilters(() => undefined);

  readonly canCreate = computed(() => this.auth.has(Perm.verificationCreate));
  readonly canWork = computed(() => this.auth.has(Perm.verificationWork));
  readonly myId = computed(() => this.auth.user()?.id ?? -1);

  readonly columns: GridColumn<VerificationSummaryDto>[] = [
    {
      key: 'number', header: 'Number', sortable: true, minWidth: 120, cellClass: 'mono nowrap',
      cell: (v) => v.verificationNumber,
      filter: { kind: 'text', placeholder: 'VER-' },
    },
    {
      key: 'target', header: 'What is being checked', sortable: true, minWidth: 220,
      cell: (v) => v.title,
      // Matched against both lines the cell prints, so searching for either finds the row.
      filterValue: (v) => `${v.title} ${v.targetSummary ?? ''}`,
      filter: { kind: 'text', placeholder: 'Contains…' },
    },
    {
      key: 'priority', header: 'Priority', sortable: true,
      cell: (v) => v.priority, sortValue: (v) => PRIORITY_ORDER[v.priority],
      filter: {
        kind: 'select', placeholder: 'Any',
        options: PRIORITIES.map((p) => ({ value: p, label: priorityLabel(p) })),
      },
    },
    {
      key: 'status', header: 'Status', sortable: true, minWidth: 150, cellClass: 'nowrap',
      cell: (v) => v.status, sortValue: (v) => STATUS_ORDER[v.status],
      filter: {
        kind: 'select', placeholder: 'Any',
        options: STATUSES.map((s) => ({ value: s, label: verificationStatusLabel(s) })),
      },
    },
    {
      // A name rather than a people dropdown: the assignable-checkers endpoint is behind
      // Verification.Create, which a checker reading their own list need not hold.
      key: 'checker', header: 'Checker', sortable: true, minWidth: 130, cellClass: 'nowrap',
      cell: (v) => v.assignedToDisplayName,
      filter: { kind: 'text', placeholder: 'Name, or -' },
    },
    {
      key: 'raised', header: 'Raised', sortable: true, cellClass: 'nowrap',
      cell: (v) => v.requestedAt,
      filter: { kind: 'date' },
    },
    {
      key: 'outcome', header: 'Outcome', sortable: true, minWidth: 170,
      cell: (v) => v.result,
      filter: {
        kind: 'select', placeholder: 'Any',
        options: RESULTS.map((r) => ({ value: r, label: verificationResultLabel(r) })),
      },
    },
    { key: 'action', header: 'Actions', headerHidden: true, align: 'right', cellClass: 'nowrap' },
  ];

  /**
   * Every tile is always present, even at zero, so its position can be learned.
   *
   * Counted from `all()` and deliberately **not** from the filtered rows: a tile says how many
   * there are in that state, and narrowing it by the column being typed into would send every
   * number towards zero as you type.
   */
  readonly tiles = computed<StatusCountDto[]>(() => {
    const rows = this.all();
    const count = (status: VerificationStatus) => rows.filter((r) => r.status === status).length;

    return [
      { key: 'Requested', label: 'Waiting for a checker', count: count('Requested') },
      { key: 'Assigned', label: 'Assigned', count: count('Assigned') },
      { key: 'InProgress', label: 'Being checked', count: count('InProgress') },
      { key: 'Completed', label: 'Checked', count: count('Completed') },
      { key: 'Cancelled', label: 'Called off', count: count('Cancelled') },
    ];
  });

  /** The tile's own narrowing. The column filters and the ordering are the grid's job. */
  readonly inView = computed(() => {
    const status = this.view();
    return status ? this.all().filter((v) => v.status === status) : this.all();
  });

  ngOnInit(): void {
    this.load();
    syncOn([this.realtime.verificationChanged], () => this.load(), this.destroyRef);
  }

  load(): void {
    this.api.verifications({ pageSize: 200 }).subscribe({
      next: (result: PagedResult<VerificationSummaryDto>) => {
        this.all.set(result.items);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  setView(key: string | null): void {
    this.view.set(key as VerificationStatus | null);
  }

  open(v: VerificationSummaryDto): void {
    void this.router.navigate(['/verifications', v.id]);
  }

  raise(): void {
    this.dialog.open(VerificationCreateDialog, { width: '640px' })
      .afterClosed()
      .subscribe((created?: { id: number }) => {
        if (created) void this.router.navigate(['/verifications', created.id]);
      });
  }

  claim(v: VerificationSummaryDto): void {
    this.api.claimVerification(v.id).subscribe(() => {
      this.toast.success(`${v.verificationNumber} is yours.`);
      void this.router.navigate(['/verifications', v.id]);
    });
  }

  assign(v: VerificationSummaryDto): void {
    this.dialog
      .open(VerificationAssignDialog, {
        data: {
          verificationId: v.id,
          verificationNumber: v.verificationNumber,
          currentCheckerId: v.assignedToUserId ?? null,
          currentCheckerName: v.assignedToDisplayName ?? null,
        },
      })
      .afterClosed()
      .subscribe((updated?: unknown) => { if (updated) this.load(); });
  }

  isFinished = (v: VerificationSummaryDto) =>
    v.status === 'Completed' || v.status === 'Cancelled';

  since = (iso: string) => sinceLabel(iso);
}
