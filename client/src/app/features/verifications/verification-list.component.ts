import { DestroyRef, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
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
import { ChipComponent, EmptyComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';
import { StatusTilesComponent } from '../../shared/status-tiles.component';
import { SortHeaderComponent, SortState } from '../../shared/sort-header.component';
import {
  ColumnFilterComponent, ColumnFilterSpec, FilterSummaryComponent, NoMatchesComponent,
  columnFilters,
} from '../../shared/column-filter.component';
import { VerificationCreateDialog } from './verification-create-dialog.component';
import { VerificationAssignDialog } from './verification-assign-dialog.component';

/** Several values for one column travel pipe-separated. Mirrors `ColumnFilters.Many` server-side. */
const SEPARATOR = '|';

/**
 * Checks: assigned investigation into whether something is actually broken.
 *
 * A standard grid — tiles, a filter row generated from the grid's own columns, sortable headings,
 * and the table kept on screen when a filter matches nothing. The one departure from the paged
 * grids is that the filtering and sorting happen **here** rather than on the server, and that is
 * correct for the same reason it is on the daily report: the whole set is loaded in one call, so
 * narrowing it locally cannot misreport a total. A check is only ever raised for a request somebody
 * could not decide, so this is tens of rows.
 *
 * That also means the tiles and the list are counted from the same array and cannot disagree — the
 * failure mode the "column filters never touch the tile counts" rule exists to prevent server-side.
 */
@Component({
  selector: 'app-verification-list',
  standalone: true,
  imports: [
    DatePipe, RouterLink, MatButtonModule, MatIconModule, MatTableModule, PageHeaderComponent,
    EmptyComponent, LoadingComponent, ChipComponent, StatusTilesComponent, SortHeaderComponent,
    ColumnFilterComponent, FilterSummaryComponent, NoMatchesComponent,
  ],
  template: `
    <div class="page">
      <app-page-header
        title="Checks"
        subtitle="Investigations into whether something is actually broken. A check never creates work by itself.">
        @if (canCreate()) {
          <button matButton="filled" (click)="raise()">
            <mat-icon>add</mat-icon> Raise a check
          </button>
        }
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon> Refresh</button>
      </app-page-header>

      <app-status-tiles
        [counts]="tiles()"
        [selected]="view()"
        [total]="all().length"
        (pick)="setView($event)" />

      <app-filter-summary [count]="filters.activeCount()" (clear)="filters.clear()" />

      <div class="card">
        @if (loading()) {
          <app-loading />
        } @else if (all().length === 0) {
          <app-empty
            message="Nothing has been sent for checking"
            icon="fact_check"
            hint="A reviewer who cannot tell whether a request is a real problem can send it here instead of guessing." />
        } @else {
          <div class="table-scroll">
            <table mat-table [dataSource]="rows()">
              <ng-container matColumnDef="number">
                <th mat-header-cell *matHeaderCellDef>
                  <app-sort-header label="Number" column="number" [sort]="sort()"
                                   (sortChange)="sort.set($event)" />
                </th>
                <td mat-cell *matCellDef="let v" class="mono nowrap">
                  <a [routerLink]="['/verifications', v.id]">{{ v.verificationNumber }}</a>
                </td>
              </ng-container>

              <ng-container matColumnDef="target">
                <th mat-header-cell *matHeaderCellDef>
                  <app-sort-header label="What is being checked" column="target" [sort]="sort()"
                                   (sortChange)="sort.set($event)" />
                </th>
                <td mat-cell *matCellDef="let v">
                  <a [routerLink]="['/verifications', v.id]">{{ v.title }}</a>
                  @if (v.targetSummary && v.targetSummary !== v.title) {
                    <div class="muted small">{{ v.targetSummary }}</div>
                  }
                </td>
              </ng-container>

              <ng-container matColumnDef="priority">
                <th mat-header-cell *matHeaderCellDef>
                  <app-sort-header label="Priority" column="priority" [sort]="sort()"
                                   (sortChange)="sort.set($event)" />
                </th>
                <td mat-cell *matCellDef="let v"><app-chip [value]="v.priority" kind="priority" /></td>
              </ng-container>

              <ng-container matColumnDef="status">
                <th mat-header-cell *matHeaderCellDef>
                  <app-sort-header label="Status" column="status" [sort]="sort()"
                                   (sortChange)="sort.set($event)" />
                </th>
                <td mat-cell *matCellDef="let v" class="nowrap">
                  <app-chip [value]="v.status" kind="verificationStatus" />
                </td>
              </ng-container>

              <!--
                A name, not a dropdown of people — the same rule the task grid follows. "-" means
                nobody has it, which is the one answer with no name to type and the one worth
                looking for most.
              -->
              <ng-container matColumnDef="checker">
                <th mat-header-cell *matHeaderCellDef>
                  <app-sort-header label="Checker" column="checker" [sort]="sort()"
                                   (sortChange)="sort.set($event)" />
                </th>
                <td mat-cell *matCellDef="let v" class="nowrap">
                  @if (v.assignedToDisplayName) {
                    <span class="truncate">{{ v.assignedToDisplayName }}</span>
                  } @else { <span class="muted">—</span> }
                </td>
              </ng-container>

              <ng-container matColumnDef="raised">
                <th mat-header-cell *matHeaderCellDef>
                  <app-sort-header label="Raised" column="raised" [sort]="sort()"
                                   (sortChange)="sort.set($event)" />
                </th>
                <td mat-cell *matCellDef="let v" class="nowrap"
                    [title]="v.requestedAt | date: 'MMM d, y HH:mm'">
                  {{ since(v.requestedAt) }}
                </td>
              </ng-container>

              <ng-container matColumnDef="outcome">
                <th mat-header-cell *matHeaderCellDef>
                  <app-sort-header label="Outcome" column="outcome" [sort]="sort()"
                                   (sortChange)="sort.set($event)" />
                </th>
                <td mat-cell *matCellDef="let v">
                  @if (v.result) {
                    <app-chip [value]="v.result" kind="verificationResult" />
                  } @else { <span class="muted">—</span> }
                </td>
              </ng-container>

              <ng-container matColumnDef="action">
                <th mat-header-cell *matHeaderCellDef aria-label="Actions"></th>
                <td mat-cell *matCellDef="let v" class="nowrap">
                  @if (v.status === 'Requested' && canWork()) {
                    <button matButton (click)="claim(v); $event.stopPropagation()">Take it</button>
                  } @else if (v.status === 'Requested' && canCreate()) {
                    <button matButton (click)="assign(v); $event.stopPropagation()">Assign</button>
                  } @else if (v.assignedToUserId === myId() && !isFinished(v)) {
                    <a matButton [routerLink]="['/verifications', v.id]"
                       (click)="$event.stopPropagation()">Open</a>
                  }
                </td>
              </ng-container>

              <!--
                The filter row. One cell per column, generated from the same list the header uses,
                so it cannot fall out of step with it — a column added here gets a filter if a spec
                names it and an empty cell if not, rather than shifting everything one place left.
              -->
              @for (column of columns; track column) {
                <ng-container [matColumnDef]="column + '_filter'">
                  <th mat-header-cell *matHeaderCellDef class="filter-cell">
                    <app-column-filter [spec]="spec(column)" [value]="filters.value(column)"
                                       (changed)="filters.set(spec(column), column, $event)" />
                  </th>
                </ng-container>
              }

              <tr mat-header-row *matHeaderRowDef="columns"></tr>
              <tr mat-header-row *matHeaderRowDef="filterRow" class="filter-row"></tr>
              <tr mat-row *matRowDef="let row; columns: columns"
                  class="clickable" tabindex="0"
                  (click)="open(row)" (keydown.enter)="open(row)"></tr>
            </table>
          </div>

          <!--
            The table stays put when nothing matches. Taking it away would take the filter row with
            it, removing the only control that can undo the problem.
          -->
          @if (rows().length === 0) {
            <app-no-matches message="No checks match those filters."
                            (clear)="clearEverything()" />
          }
        }
      </div>
    </div>
  `,
  styles: `
    table { width: 100%; }
    .mono { font-variant-numeric: tabular-nums; }
    .truncate { display: inline-block; max-width: 180px; }
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

  readonly columns = [
    'number', 'target', 'priority', 'status', 'checker', 'raised', 'outcome', 'action',
  ];
  readonly filterRow = this.columns.map((c) => `${c}_filter`);

  readonly loading = signal(true);
  readonly sort = signal<SortState>({ by: null, descending: true });

  /** The status tile that is lit, or null for all. */
  readonly view = signal<VerificationStatus | null>(null);

  /** Every check the caller may see. The tiles and the list are both counted from this. */
  readonly all = signal<VerificationSummaryDto[]>([]);

  /**
   * Nothing to re-fetch: the rows are already here, so the filter row only has to invalidate the
   * computed. Passing a no-op keeps the debounce-on-typing behaviour every other grid has.
   */
  readonly filters = columnFilters(() => undefined);

  readonly canCreate = computed(() => this.auth.has(Perm.verificationCreate));
  readonly canWork = computed(() => this.auth.has(Perm.verificationWork));
  readonly myId = computed(() => this.auth.user()?.id ?? -1);

  private readonly specs: Record<string, ColumnFilterSpec> = {
    number: { key: 'number', kind: 'text', placeholder: 'VER-', minWidth: 120 },
    target: { key: 'target', kind: 'text', placeholder: 'Contains…', minWidth: 220 },
    priority: {
      key: 'priority', kind: 'select', placeholder: 'Any',
      options: (['Critical', 'High', 'Normal', 'Low'] as Priority[])
        .map((p) => ({ value: p, label: priorityLabel(p) })),
    },
    status: {
      key: 'status', kind: 'select', placeholder: 'Any', minWidth: 150,
      options: (['Requested', 'Assigned', 'InProgress', 'Completed', 'Cancelled'] as VerificationStatus[])
        .map((s) => ({ value: s, label: verificationStatusLabel(s) })),
    },
    // A name rather than a people dropdown: the assignable-checkers endpoint is behind
    // Verification.Create, which a checker reading their own list need not hold.
    checker: { key: 'checker', kind: 'text', placeholder: 'Name, or -', minWidth: 130 },
    raised: { key: 'raised', kind: 'date' },
    outcome: {
      key: 'outcome', kind: 'select', placeholder: 'Any', minWidth: 170,
      options: ([
        'IssueConfirmed', 'WorkingCorrectly', 'ConfigurationOrDataIssue', 'NeedsClarification',
        'Inconclusive',
      ] as VerificationResult[]).map((r) => ({ value: r, label: verificationResultLabel(r) })),
    },
  };

  spec = (column: string): ColumnFilterSpec | undefined => this.specs[column];

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

  readonly rows = computed(() => {
    const status = this.view();
    const values = this.filters.current();
    const { by, descending } = this.sort();

    let rows = this.all();
    if (status) rows = rows.filter((v) => v.status === status);

    for (const [key, raw] of Object.entries(values)) {
      if (!raw) continue;
      rows = rows.filter((v) => this.matches(v, key, raw));
    }

    if (!by) return rows;

    const direction = descending ? -1 : 1;
    // Copied before sorting: the array is a signal's value and sorting in place would mutate it.
    return [...rows].sort((a, b) => direction * this.compare(a, b, by));
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

  /** The empty-state button clears the tile as well as the columns — both are narrowing the list. */
  clearEverything(): void {
    this.view.set(null);
    this.filters.clear();
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

  // --- filtering ---------------------------------------------------------------------------

  /**
   * One column's filter against one row. Within a column several values are OR'd; across columns
   * they are AND'd, which is the rule the server applies and the one people expect.
   */
  private matches(v: VerificationSummaryDto, key: string, raw: string): boolean {
    // Free text is never split, so a search term may legitimately contain a pipe.
    const anyOf = (value: string | null | undefined) =>
      raw.split(SEPARATOR).some((wanted) => wanted === value);

    switch (key) {
      case 'number': return contains(v.verificationNumber, raw);
      case 'target': return contains(v.title, raw) || contains(v.targetSummary, raw);
      case 'priority': return anyOf(v.priority);
      case 'status': return anyOf(v.status);
      case 'outcome': return anyOf(v.result ?? null);

      // "-" is how you ask for the ones nobody has, matching the task grid's assignee column.
      case 'checker':
        return raw.trim() === '-'
          ? !v.assignedToDisplayName
          : contains(v.assignedToDisplayName, raw);

      // Local, not UTC. The column prints a local date, so filtering by a different day boundary
      // than the one on screen is the mismatch the business-calendar rule exists to avoid — and on
      // the client the browser's zone is the closest thing to the business day.
      case 'raised': return localDay(v.requestedAt) === raw;

      // A key nobody handles filters nothing, rather than emptying the grid.
      default: return true;
    }
  }

  private compare(a: VerificationSummaryDto, b: VerificationSummaryDto, by: string): number {
    switch (by) {
      case 'number': return a.verificationNumber.localeCompare(b.verificationNumber);
      case 'target': return a.title.localeCompare(b.title);
      // By urgency, not alphabetically: Critical must sort above High.
      case 'priority': return PRIORITY_ORDER[a.priority] - PRIORITY_ORDER[b.priority];
      // By where it has got to, so a queue reads in the order it moves through.
      case 'status': return STATUS_ORDER[a.status] - STATUS_ORDER[b.status];
      case 'checker':
        return (a.assignedToDisplayName ?? '').localeCompare(b.assignedToDisplayName ?? '');
      case 'outcome': return (a.result ?? '').localeCompare(b.result ?? '');
      case 'raised':
      default: return a.requestedAt.localeCompare(b.requestedAt);
    }
  }
}

const PRIORITY_ORDER: Record<Priority, number> = {
  Critical: 0, High: 1, Normal: 2, Low: 3,
};

const STATUS_ORDER: Record<VerificationStatus, number> = {
  Requested: 0, Assigned: 1, InProgress: 2, Completed: 3, Cancelled: 4,
};

function contains(value: string | null | undefined, term: string): boolean {
  return (value ?? '').toLowerCase().includes(term.trim().toLowerCase());
}

/** `yyyy-MM-dd` in the browser's zone — the same day the column prints. */
function localDay(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}
