import { DestroyRef, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RealtimeService } from '../../core/realtime.service';
import { syncOn } from '../../core/realtime-sync';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../../core/api.service';
import { StatusTilesComponent } from '../../shared/status-tiles.component';
import { SortHeaderComponent, SortState } from '../../shared/sort-header.component';
import { AuthService } from '../../core/auth.service';
import { Perm } from '../../core/permissions';
import {
  PagedResult, RequestedUrgency, RequestStatus, RequestSummaryDto, ClientOptionDto,
  RequestBatchSummaryDto, StatusCountDto,
} from '../../core/models';
import { urgencyLabel } from '../../core/labels';
import { SearchSelectComponent } from '../../shared/search-select.component';
import { ChipComponent, EmptyComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';
import { QuickViewComponent, QuickViewTarget } from '../../shared/quick-view.component';
import {
  ColumnFilterComponent, ColumnFilterSpec, FilterSummaryComponent, NoMatchesComponent,
  columnFilters,
} from '../../shared/column-filter.component';

const URGENCIES: RequestedUrgency[] = ['Critical', 'High', 'Normal', 'Low'];

const STATUSES: RequestStatus[] = [
  'Submitted', 'InReview', 'ClarificationRequired', 'Approved', 'Rejected', 'Duplicate',
  'Deferred', 'Escalated',
];

@Component({
  selector: 'app-request-list',
  standalone: true,
  imports: [
    DatePipe, RouterLink, MatButtonModule, MatIconModule,
    MatTableModule, MatPaginatorModule,
    MatTooltipModule, QuickViewComponent, PageHeaderComponent, ChipComponent, EmptyComponent, LoadingComponent,
    StatusTilesComponent, SortHeaderComponent, ColumnFilterComponent, NoMatchesComponent,
    FilterSummaryComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Requests" subtitle="Everything that has been asked for.">
        @if (auth.has(Perm.requestCreate)) {
          <a matButton="filled" routerLink="/requests/new">
            <mat-icon>add</mat-icon> New request
          </a>
        }
      </app-page-header>

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
          <span class="muted small">Asked for as a set:</span>
          @for (batch of batches(); track batch.id) {
            <a class="batch-pill" [routerLink]="['/requests/batches', batch.id]">
              <span class="mono small">{{ batch.batchNumber }}</span>
              <span class="truncate">{{ batch.title }}</span>
              <span class="muted small nowrap">{{ batch.itemCount }} items</span>
            </a>
          }
        </div>
      }

      <app-filter-summary [count]="filters.activeCount()" (clear)="filters.clear()" />

      <div class="card" [class.refreshing]="refreshing()">
        @if (loading()) {
          <app-loading />
        } @else if (page().items.length === 0 && !filters.any()) {
          <app-empty message="No requests match" icon="inbox"
                     hint="Pick a different status card above."
                     actionLabel="Raise a request" actionRoute="/requests/new" />
        } @else {
          <div class="table-scroll">
            <table mat-table [dataSource]="page().items">
              <ng-container matColumnDef="number">
                <th mat-header-cell *matHeaderCellDef>
                  <app-sort-header label="Request" column="number" [sort]="sort()"
                                   (sortChange)="applySort($event)" />
                </th>
                <td mat-cell *matCellDef="let r">
                  <a class="mono link" [routerLink]="['/requests', r.id]">{{ r.requestNumber }}</a>
                </td>
              </ng-container>

              <ng-container matColumnDef="title">
                <th mat-header-cell *matHeaderCellDef>
                  <app-sort-header label="Title" column="title" [sort]="sort()"
                                   (sortChange)="applySort($event)" />
                </th>
                <td mat-cell *matCellDef="let r">
                  <a class="title" [routerLink]="['/requests', r.id]">{{ r.title }}</a>
                  @if (r.hasOpenClarification) {
                    <mat-icon class="flag" matTooltip="Waiting on a clarification">help</mat-icon>
                  }
                </td>
              </ng-container>

              <!--
                The status shown is the one the server decided for this reader: for a requester
                that follows the task their request generated, because after approval the request
                itself stops moving and "Approved" would sit there for a fortnight while the work
                was actually being done.
              -->
              <ng-container matColumnDef="status">
                <th mat-header-cell *matHeaderCellDef>
                  <app-sort-header label="Status" column="status" [sort]="sort()"
                                   (sortChange)="applySort($event)" />
                </th>
                <td mat-cell *matCellDef="let r">
                  <span class="chip" [class]="'tone-' + tone(r)">{{ r.viewLabel }}</span>
                </td>
              </ng-container>

              <ng-container matColumnDef="client">
                <th mat-header-cell *matHeaderCellDef>
                  <app-sort-header label="Client" column="client" [sort]="sort()"
                                   (sortChange)="applySort($event)" />
                </th>
                <td mat-cell *matCellDef="let r">
                  @if (r.clientName) {
                    <span class="truncate">{{ r.clientName }}</span>
                  } @else { <span class="muted small">Internal</span> }
                </td>
              </ng-container>

              <!-- Who is doing it, once someone is. The requester's most-asked question. -->
              <ng-container matColumnDef="responsible">
                <th mat-header-cell *matHeaderCellDef>Responsible person</th>
                <td mat-cell *matCellDef="let r">
                  @if (r.responsibleDisplayName) {
                    <span class="truncate">{{ r.responsibleDisplayName }}</span>
                  } @else { <span class="muted small">Nobody yet</span> }
                </td>
              </ng-container>

              <ng-container matColumnDef="updated">
                <th mat-header-cell *matHeaderCellDef>Updated</th>
                <td mat-cell *matCellDef="let r" class="nowrap">
                  {{ (r.updatedAt ?? r.requestedAt) | date: 'MMM d' }}
                </td>
              </ng-container>

              <!--
                One action, the one that is actually waiting on this reader. Anything rarer is on
                the detail screen rather than crowding every row.
              -->
              <ng-container matColumnDef="action">
                <th mat-header-cell *matHeaderCellDef aria-label="Actions"></th>
                <td mat-cell *matCellDef="let r" class="action-cell">
                  <!-- A look without leaving the list. Chrome, not a column; desktop only. -->
                  <button matIconButton class="peek" type="button" matTooltip="Quick look"
                          [attr.aria-label]="'Quick look at ' + r.requestNumber"
                          (click)="peek(r); $event.stopPropagation()">
                    <mat-icon>visibility</mat-icon>
                  </button>
                  @if (r.hasOpenClarification) {
                    <a class="action" [routerLink]="['/requests', r.id]"
                       (click)="$event.stopPropagation()">Reply</a>
                  }
                </td>
              </ng-container>

              <ng-container matColumnDef="urgency">
                <th mat-header-cell *matHeaderCellDef>
                  <app-sort-header label="Urgency" column="urgency" [sort]="sort()"
                                   (sortChange)="applySort($event)" />
                </th>
                <td mat-cell *matCellDef="let r">
                  <app-chip [value]="r.requestedUrgency" kind="urgency" />
                </td>
              </ng-container>

              <ng-container matColumnDef="requester">
                <th mat-header-cell *matHeaderCellDef>
                  <app-sort-header label="Requested by" column="requester" [sort]="sort()"
                                   (sortChange)="applySort($event)" />
                </th>
                <td mat-cell *matCellDef="let r">{{ r.requestedByDisplayName }}</td>
              </ng-container>

              <ng-container matColumnDef="raised">
                <th mat-header-cell *matHeaderCellDef>
                  <app-sort-header label="Requested on" column="raised" [sort]="sort()"
                                   (sortChange)="applySort($event)" />
                </th>
                <td mat-cell *matCellDef="let r" class="nowrap">
                  {{ r.requestedAt | date: 'MMM d' }}
                </td>
              </ng-container>

              <ng-container matColumnDef="task">
                <th mat-header-cell *matHeaderCellDef>Task</th>
                <td mat-cell *matCellDef="let r">
                  @if (r.generatedTaskId) {
                    <a class="link mono small" [routerLink]="['/tasks', r.generatedTaskId]">Open</a>
                  } @else { <span class="muted">—</span> }
                </td>
              </ng-container>

              <!--
                The filter row. One cell per column in columns(), so it cannot fall out of step
                with the header above it — a column added to the grid gets a filter if a spec names
                it and an empty cell if not, rather than shifting everything one place left.
              -->
              @for (column of columns(); track column) {
                <ng-container [matColumnDef]="column + '_filter'">
                  <th mat-header-cell *matHeaderCellDef class="filter-cell">
                    <app-column-filter [spec]="spec(column)" [value]="filters.value(column)"
                                       (changed)="filters.set(spec(column), column, $event)" />
                  </th>
                </ng-container>
              }

              <tr mat-header-row *matHeaderRowDef="columns()"></tr>
              <tr mat-header-row *matHeaderRowDef="filterRow()" class="filter-row"></tr>
              <tr mat-row *matRowDef="let row; columns: columns()"
                  class="clickable" tabindex="0"
                  (click)="open(row)" (keydown.enter)="open(row)"></tr>
            </table>
          </div>
          @if (page().items.length === 0) {
            <app-no-matches message="No requests match those filters."
                            (clear)="filters.clear()" />
          } @else {
            <mat-paginator [length]="page().totalCount" [pageSize]="page().pageSize"
                           [pageIndex]="page().page - 1" [pageSizeOptions]="[25, 50]"
                           (page)="onPage($event)" />
          }
        }
      </div>

      <app-quick-view [target]="peeking()" (close)="peeking.set(null)" />
    </div>
  `,
  styles: `
    /*
     * A filter reload dims the *rows* rather than replacing the table — see the note on the loaded flag.
     * Deliberately not the whole card and deliberately no pointer-events block: the filter row is
     * what triggered the reload, and freezing it would stop the next keystroke landing.
     */
    .refreshing tbody { opacity: .45; transition: opacity .12s; }

    .batch-strip {
      display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin: 14px 0 -2px;
    }
    .batch-pill {
      display: inline-flex; align-items: center; gap: 8px; max-width: 340px;
      border: 1px solid var(--border); border-radius: 999px; padding: 5px 14px;
      background: var(--surface-raised); color: inherit; text-decoration: none; font-size: 13px;
    }
    .batch-pill:hover { background: var(--surface-sunken); }
    .action-cell { text-align: right; white-space: nowrap; }
    .peek { margin-right: 2px; vertical-align: middle; }
    .peek mat-icon { font-size: 18px; width: 18px; height: 18px; color: var(--text-muted); }
    @media (max-width: 1100px) { .peek { display: none; } }
    .filters { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; margin-bottom: 16px; }
    .filters mat-form-field { margin-bottom: -1.25em; }
    .filters app-search-select { width: 200px; margin-bottom: -1.25em; }
    .search { flex: 1 1 240px; }
    tr.clickable { cursor: pointer; }
    tr.clickable:hover { background: var(--surface-sunken); }
    tr.clickable:focus-visible { outline: 2px solid #1d69d4; outline-offset: -2px; }
    .action-cell { text-align: right; }
    .action {
      border: 1px solid var(--border-strong); background: var(--surface);
      border-radius: 7px; padding: 4px 12px; font: inherit; font-size: 12.5px;
      font-weight: 500; cursor: pointer; text-decoration: none; color: inherit;
    }
    .action:hover { background: var(--surface-sunken); }
    .link { color: var(--text-muted); text-decoration: none; }
    .link:hover { text-decoration: underline; }
    .title { color: inherit; text-decoration: none; font-weight: 500; }
    .title:hover { text-decoration: underline; }
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

  /**
   * A requester is not shown the task their request generated. The request is their record of it,
   * and the columns above already say who has it and how it is going — sending them to a second
   * screen to learn "what happened after approval" is the thing this list exists to avoid.
   *
   * People who coordinate keep the intake-shaped grid: for them a request *is* an inbox item.
   */
  readonly columns = computed(() => this.auth.has(Perm.requestViewAll)
    ? ['number', 'title', 'client', 'status', 'urgency', 'requester', 'raised', 'action']
    : ['number', 'title', 'client', 'status', 'responsible', 'updated', 'action']);

  readonly view = signal<string | null>(null);

  /**
   * The filter row. It replaced a card holding a search box, a client dropdown and an "only mine"
   * toggle — three controls above the grid that each described one column below it. Requester is
   * where "only mine" went: filtering that column by a person answers the same question and every
   * other one like it.
   */
  readonly filters = columnFilters(() => { this.pageIndex = 0; this.reload(); });

  /**
   * What each column can be filtered by. Built as a signal because two of them are populated from
   * the server (clients, people) and one depends on who is looking.
   */
  /** Which values each column can still be narrowed by, from the server. See the tasks grid. */
  readonly options = signal<Record<string, string[]> | null>(null);

  private availableFor(key: string): ReadonlySet<string> | undefined {
    const all = this.options();
    return all?.[key] ? new Set(all[key]) : undefined;
  }

  readonly specs = computed<Record<string, ColumnFilterSpec>>(() => ({
    number: { key: 'number', kind: 'text', placeholder: 'REQ-…' },
    title: { key: 'title', kind: 'text', placeholder: 'Title', minWidth: 260 },
    client: {
      key: 'client', kind: 'select', placeholder: 'Any client',
      options: this.clients().map((c) => ({ value: c.id, label: c.name })),
      available: this.availableFor('client'),
    },
    urgency: {
      key: 'urgency', kind: 'select', placeholder: 'Any',
      options: URGENCIES.map((u) => ({ value: u, label: urgencyLabel(u) })),
      available: this.availableFor('urgency'),
    },
    // Text, not a dropdown: the list of people is behind Task.Assign, which a reviewer need not
    // hold, and a filter that 403s for half its users is worse than one that matches on the name
    // already printed in the column.
    requester: { key: 'requester', kind: 'text', placeholder: 'Name' },
    responsible: { key: 'responsible', kind: 'text', placeholder: 'Name' },
    raised: { key: 'raised', kind: 'date' },
  }));

  spec = (column: string): ColumnFilterSpec | undefined => this.specs()[column];

  /** One filter cell per column, named so Material can pair the two header rows up. */
  readonly filterRow = computed(() => this.columns().map((c) => `${c}_filter`));

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
    const view = this.route.snapshot.queryParamMap.get('view');

    // A reviewer opens this page to answer "what is waiting for me?", so that is what it opens on
    // — the All list buries one actionable row under everything ever raised. Only for people who
    // actually triage: a requester opening on "Submitted" would be shown the one part of their
    // work that has *not* started yet and hidden everything in progress, which is the opposite of
    // what they came for. A `view` in the URL always wins, so a link still lands where it points.
    if (view) {
      this.view.set(view);
    } else if (this.auth.has(Perm.taskReview)) {
      this.view.set('submitted');
    }

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
