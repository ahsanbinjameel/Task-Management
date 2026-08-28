import { DestroyRef, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RealtimeService } from '../../core/realtime.service';
import { syncOn } from '../../core/realtime-sync';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { PageEvent } from '@angular/material/paginator';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../../core/api.service';
import { StatusTilesComponent } from '../../shared/status-tiles.component';
import { SortState } from '../../shared/sort-header.component';
import { AuthService } from '../../core/auth.service';
import { Perm } from '../../core/permissions';
import {
  PagedResult, RequestedUrgency, RequestStatus, RequestSummaryDto, ClientOptionDto,
  RequestBatchSummaryDto, StatusCountDto,
} from '../../core/models';
import { urgencyLabel } from '../../core/labels';
import { ChipComponent, PageHeaderComponent } from '../../shared/ui';
import { ViewTabsComponent } from '../../shared/view-tabs.component';
import { QuickViewComponent, QuickViewTarget } from '../../shared/quick-view.component';
import { columnFilters } from '../../shared/column-filter.component';
import { DataGridComponent, GridCellDirective, GridColumn } from '../../shared/data-grid.component';

const URGENCIES: RequestedUrgency[] = ['Critical', 'High', 'Normal', 'Low'];

const STATUSES: RequestStatus[] = [
  'Submitted', 'InReview', 'ClarificationRequired', 'Approved', 'Rejected', 'Duplicate',
  'Deferred', 'Escalated',
];

@Component({
  selector: 'app-request-list',
  standalone: true,
  imports: [
    RouterLink, MatButtonModule, MatIconModule, MatTooltipModule,
    QuickViewComponent, PageHeaderComponent, ChipComponent, StatusTilesComponent,
    DataGridComponent, GridCellDirective, ViewTabsComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Requests">
        @if (auth.has(Perm.requestCreate)) {
          <a matButton="filled" routerLink="/requests/new">
            <mat-icon>add</mat-icon> New request
          </a>
        }
      </app-page-header>

      <app-view-tabs group="requests" />

      <app-status-tiles
        [counts]="counts()"
        [selected]="view()"
        [total]="totalAcross()"
        (pick)="pickView($event)" />

      <!--
        Submissions the caller made as a set. A strip rather than a section: the items are already
        in the table below, each with its own row and its own status — this is only a way back to
        the whole submission, for somebody who raised eight things and wants to see how the eight
        are getting on together.
      -->
      @if (batches().length > 0) {
        <div class="batch-strip">
          @for (batch of batches(); track batch.id) {
            <a class="batch-pill" [routerLink]="['/requests/batches', batch.id]">
              <span class="mono small">{{ batch.batchNumber }}</span>
              <span class="truncate">{{ batch.title }}</span>
              <span class="muted small nowrap">{{ batch.itemCount }} items</span>
            </a>
          }
        </div>
      }

      <app-data-grid
        [rows]="page().items" [columns]="columns()"
        [loading]="loading()" [refreshing]="refreshing()"
        [filters]="filters" [filterOptions]="options()" [externalFilter]="view() !== null"
        [sort]="sort()" (sortChange)="applySort($event)"
        [total]="page().totalCount" [pageSize]="page().pageSize"
        [pageIndex]="page().page - 1" [pageSizeOptions]="[25, 50]" (pageChange)="onPage($event)"
        emptyMessage="No requests match" emptyIcon="inbox"
        emptyActionLabel="Raise a request" emptyActionRoute="/requests/new"
        noMatchesMessage="No requests match those filters."
        [clickable]="true" (rowClick)="open($event)" (clearedFilters)="clearView()">

        <ng-template gridCell="number" let-r>
          <a class="grid-link mono" [routerLink]="['/requests', r.id]">{{ r.requestNumber }}</a>
        </ng-template>

        <ng-template gridCell="title" let-r>
          <a class="grid-title" [routerLink]="['/requests', r.id]">{{ r.title }}</a>
          @if (r.hasOpenClarification) {
            <mat-icon class="flag" matTooltip="Waiting on a clarification">help</mat-icon>
          }
        </ng-template>

        <!--
          The status shown is the one the server decided for this reader: for a requester that
          follows the task their request generated, because after approval the request itself stops
          moving and "Approved" would sit there for a fortnight while the work was being done.
        -->
        <ng-template gridCell="status" let-r>
          <span class="chip" [class]="'tone-' + tone(r)">{{ r.viewLabel }}</span>
        </ng-template>

        <ng-template gridCell="client" let-r>
          @if (r.clientName) {
            <span class="truncate">{{ r.clientName }}</span>
          } @else { <span class="muted small">Internal</span> }
        </ng-template>

        <ng-template gridCell="responsible" let-r>
          @if (r.responsibleDisplayName) {
            <span class="truncate">{{ r.responsibleDisplayName }}</span>
          } @else { <span class="muted small">Nobody yet</span> }
        </ng-template>

        <ng-template gridCell="urgency" let-r>
          <app-chip [value]="r.requestedUrgency" kind="urgency" />
        </ng-template>

        <!--
          One action, the one that is actually waiting on this reader. Anything rarer is on the
          detail screen rather than crowding every row.
        -->
        <ng-template gridCell="action" let-r>
          <button matIconButton class="grid-peek" type="button" matTooltip="Quick look"
                  [attr.aria-label]="'Quick look at ' + r.requestNumber"
                  (click)="peek(r); $event.stopPropagation()">
            <mat-icon>visibility</mat-icon>
          </button>
          @if (r.hasOpenClarification) {
            <a class="grid-action" [routerLink]="['/requests', r.id]"
               (click)="$event.stopPropagation()">Reply</a>
          }
        </ng-template>
      </app-data-grid>

      <app-quick-view [target]="peeking()" (close)="peeking.set(null)" />
    </div>
  `,
  styles: `
    .batch-strip {
      display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin: 14px 0 -2px;
    }
    .batch-pill {
      display: inline-flex; align-items: center; gap: 8px; max-width: 340px;
      border: 1px solid var(--border); border-radius: 999px; padding: 5px 14px;
      background: var(--surface-raised); color: inherit; text-decoration: none; font-size: 13px;
    }
    .batch-pill:hover { background: var(--surface-sunken); }
    .flag {
      font-size: 15px; width: 15px; height: 15px;
      color: var(--tone-warn-fg); vertical-align: middle; margin-left: 5px;
    }
  `,
})
export class RequestListComponent implements OnInit {
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly Perm = Perm;
  readonly statuses = STATUSES;

  readonly view = signal<string | null>(null);

  /**
   * The filter row. It replaced a card holding a search box, a client dropdown and an "only mine"
   * toggle — three controls above the grid that each described one column below it. Requester is
   * where "only mine" went: filtering that column by a person answers the same question and every
   * other one like it.
   */
  readonly filters = columnFilters(() => { this.pageIndex = 0; this.reload(); });

  /** Which values each column can still be narrowed by, from the server. Merged in by the grid. */
  readonly options = signal<Record<string, string[]> | null>(null);

  /**
   * A requester is not shown the task their request generated. The request is their record of it,
   * and the columns already say who has it and how it is going — sending them to a second screen
   * to learn "what happened after approval" is the thing this list exists to avoid.
   *
   * People who coordinate keep the intake-shaped grid: for them a request *is* an inbox item.
   */
  readonly columns = computed<GridColumn<RequestSummaryDto>[]>(() => {
    const number: GridColumn<RequestSummaryDto> = {
      key: 'number', header: 'Request', sortable: true,
      filter: { kind: 'text', placeholder: 'REQ-…' },
    };
    const title: GridColumn<RequestSummaryDto> = {
      key: 'title', header: 'Title', sortable: true, minWidth: 260,
      filter: { kind: 'text', placeholder: 'Title' },
    };
    const client: GridColumn<RequestSummaryDto> = {
      key: 'client', header: 'Client', sortable: true,
      filter: {
        kind: 'select', placeholder: 'Any client',
        options: this.clients().map((c) => ({ value: c.id, label: c.name })),
      },
    };
    const status: GridColumn<RequestSummaryDto> = {
      key: 'status', header: 'Status', sortable: true,
    };
    const action: GridColumn<RequestSummaryDto> = {
      key: 'action', header: 'Actions', headerHidden: true, align: 'right', cellClass: 'nowrap',
    };

    if (this.auth.has(Perm.requestViewAll)) {
      return [
        number, title, client, status,
        {
          key: 'urgency', header: 'Urgency', sortable: true,
          filter: {
            kind: 'select', placeholder: 'Any',
            options: URGENCIES.map((u) => ({ value: u, label: urgencyLabel(u) })),
          },
        },
        // Text, not a dropdown: the list of people is behind Task.Assign, which a reviewer need
        // not hold, and a filter that 403s for half its users is worse than one that matches on
        // the name already printed in the column.
        {
          key: 'requester', header: 'Requested by', sortable: true,
          cell: (r) => r.requestedByDisplayName,
          filter: { kind: 'text', placeholder: 'Name' },
        },
        {
          key: 'raised', header: 'Requested on', sortable: true, cellClass: 'nowrap',
          cell: (r) => formatDay(r.requestedAt),
          filterValue: (r) => r.requestedAt,
          filter: { kind: 'date' },
        },
        action,
      ];
    }

    return [
      number, title, client, status,
      // Who is doing it, once someone is. The requester's most-asked question.
      {
        key: 'responsible', header: 'Responsible person',
        filter: { kind: 'text', placeholder: 'Name' },
      },
      {
        key: 'updated', header: 'Updated', cellClass: 'nowrap',
        cell: (r) => formatDay(r.updatedAt ?? r.requestedAt),
      },
      action,
    ];
  });

  readonly batches = signal<RequestBatchSummaryDto[]>([]);
  /** The row the drawer is showing, or null when it is closed. */
  readonly peeking = signal<QuickViewTarget | null>(null);

  peek(request: { id: number }): void {
    this.peeking.set({ kind: 'request', id: request.id });
  }

  /**
   * True only until the first load lands.
   *
   * A reload triggered by the filter row must **not** swap the table out for a spinner: doing so
   * destroys the filter row along with it, which closed the open multi-select panel after the first
   * tick and made choosing two values impossible. Subsequent loads dim the table in place instead.
   */
  readonly loading = signal(true);
  readonly refreshing = signal(false);
  private loaded = false;
  readonly page = signal<PagedResult<RequestSummaryDto>>(
    { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });

  private pageIndex = 0;
  private pageSize = 25;

  label = (value: string) => urgencyLabel(value as RequestedUrgency);

  ngOnInit(): void {
    // The list opens on everything. It used to open a reviewer on "Submitted", which answered
    // "what is waiting for me?" at the cost of hiding every other request they had a hand in —
    // and a tile is one click away, whereas a view you did not choose is not obviously a filter
    // at all. A `view` in the URL still wins, so a link lands where it points.
    const view = this.route.snapshot.queryParamMap.get('view');
    if (view) this.view.set(view);

    this.api.clients().subscribe((c) => this.clients.set(c));
    this.reload();

    // Re-fetch on the server's say-so; see syncOn for why it debounces and tears down.
    syncOn(
      [this.realtime.requestChanged],
      () => this.reload(),
      this.destroyRef);
  }

  readonly sort = signal<SortState>({ by: null, descending: true });

  applySort(next: SortState): void {
    this.sort.set(next);
    this.pageIndex = 0;
    this.reload();
  }

  readonly counts = signal<StatusCountDto[]>([]);
  readonly clients = signal<ClientOptionDto[]>([]);

  readonly totalAcross = computed(() => this.counts().reduce((sum, c) => sum + c.count, 0));

  /**
   * A tile is the filter; clicking the active one clears it. The choice goes into the URL so Back
   * walks back through views and a particular one can be linked to.
   */
  pickView(view: string | null): void {
    this.view.set(view);
    this.pageIndex = 0;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { view },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
    this.reload();
  }

  /** The grid's "clear filters" clears the tile too — both are narrowing the same list. */
  clearView(): void {
    if (this.view() !== null) this.pickView(null);
  }

  open(request: RequestSummaryDto): void {
    void this.router.navigate(['/requests', request.id]);
  }

  /** Colour follows the meaning of the view, not the internal status name. */
  tone(request: RequestSummaryDto): string {
    switch (request.viewKey) {
      case 'done': return 'success';
      case 'declined': return 'danger';
      case 'input': return 'warn';
      case 'waiting': return 'warn';
      case 'working': case 'checking': return 'running';
      default: return 'neutral';
    }
  }

  /** What each dropdown should still offer, given the other columns. See the tasks grid. */
  private loadFilterOptions(): void {
    this.api.requestFilterOptions({
      view: this.view() ?? undefined,
      ...this.filters.asObject(),
    }).subscribe({
      next: (o) => this.options.set(o.columns),
      error: () => this.options.set(null),
    });
  }

  /**
   * The tiles deliberately ignore the filter row.
   *
   * A tile says how many there are in that status; narrowing it by the column you are currently
   * typing into would make every tile drop towards zero as you type, and the number you were
   * navigating by would move under you. They still respect who you are, because that is not a
   * filter — it is the limit of what you may see.
   */
  private loadCounts(): void {
    this.api.requestStatusCounts({}).subscribe((c) => this.counts.set(c));
  }

  /** One place to leave a load, whether it succeeded or not. */
  private settle(): void {
    this.loaded = true;
    this.loading.set(false);
    this.refreshing.set(false);
  }

  reload(): void {
    // The caller's own, always — a batch is a way back to a submission they made, not a
    // browsing view. Reviewers reach other people's through the review queue.
    this.api.myBatches().subscribe({
      next: (p) => this.batches.set(p.items),
      error: () => this.batches.set([]),
    });

    this.loadCounts();
    this.loadFilterOptions();
    if (this.loaded) this.refreshing.set(true); else this.loading.set(true);
    this.api.requests({
      view: this.view() ?? undefined,
      sortBy: this.sort().by ?? undefined,
      sortDescending: this.sort().descending,
      page: this.pageIndex + 1,
      pageSize: this.pageSize,
      ...this.filters.asObject(),
    }).subscribe({
      next: (result) => { this.page.set(result); this.settle(); },
      error: () => this.settle(),
    });
  }

  onPage(event: PageEvent): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
    this.reload();
  }
}

/** `MMM d`, matching the DatePipe the cell templates use elsewhere in the grid. */
function formatDay(iso: string | null | undefined): string {
  if (!iso) return '';
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}
