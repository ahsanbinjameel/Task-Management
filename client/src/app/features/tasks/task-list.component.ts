import { DestroyRef, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RealtimeService } from '../../core/realtime.service';
import { syncOn } from '../../core/realtime-sync';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ApiService } from '../../core/api.service';
import { StatusTilesComponent } from '../../shared/status-tiles.component';
import { SortState } from '../../shared/sort-header.component';
import {
  ClientOptionDto, PagedResult, Priority, StatusCountDto, TaskSummaryDto, WorkTaskStatus,
} from '../../core/models';
import { enumOptions, SearchSelectComponent } from '../../shared/search-select.component';
import { taskView } from '../../shared/list-views';
import { EmptyComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';
import { TaskTableComponent } from '../../shared/task-table.component';
import { QuickViewComponent, QuickViewTarget } from '../../shared/quick-view.component';
import {
  ColumnFilterSpec, FilterSummaryComponent, NoMatchesComponent, columnFilters,
} from '../../shared/column-filter.component';
import { priorityLabel } from '../../core/labels';

const STATUSES: WorkTaskStatus[] = [
  'ReadyForAssignment', 'Assigned', 'ReadyToStart', 'InProgress', 'Paused', 'Blocked',
  'CompletedReadyForQC', 'QCReview', 'QCFailedRework', 'QCPassed', 'ReadyForClosure',
  'Closed', 'Cancelled', 'Deferred', 'OnHold', 'Reopened',
];

@Component({
  selector: 'app-task-list',
  standalone: true,
  imports: [
    MatPaginatorModule,
    PageHeaderComponent, EmptyComponent, LoadingComponent, TaskTableComponent, QuickViewComponent,
    StatusTilesComponent, NoMatchesComponent, FilterSummaryComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Tasks" subtitle="Work you are part of, and work you oversee." />

      <app-status-tiles
        [counts]="counts()"
        [selected]="view()"
        [total]="totalAcross()"
        (pick)="pickView($event)" />

      <app-filter-summary [count]="filters.activeCount()" (clear)="filters.clear()" />

      <div class="card list" [class.refreshing]="refreshing()">
        @if (loading()) {
          <app-loading />
        } @else if (page().items.length === 0 && !filters.any()) {
          <!-- Genuinely nothing here. Only reachable with no filter set — otherwise the grid stays
               up so the filter that emptied it is still reachable. -->
          <app-empty message="No work matches" icon="search_off"
                     hint="Pick a different status card above." />
        } @else {
          <app-task-table [tasks]="page().items" (action)="act($event)"
                          [columns]="grid().columns.concat(grid().action ? ['action'] : [])"
                          [actionLabel]="grid().action?.label ?? 'Open'"
                          [actionWhen]="grid().action?.when ?? null"
                          [sortable]="true" [sort]="sort()" (sortChange)="applySort($event)"
                          [filters]="filters" [specs]="specs()"
                          [showPreview]="true" (preview)="peek($event)"/>
          @if (page().items.length === 0) {
            <app-no-matches (clear)="filters.clear()" />
          } @else {
            <mat-paginator [length]="page().totalCount" [pageSize]="page().pageSize"
                           [pageIndex]="page().page - 1" [pageSizeOptions]="[25, 50, 100]"
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

    .filters { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; margin-bottom: 16px; }
    .filters mat-form-field { margin-bottom: -1.25em; }
    .filters app-search-select { width: 180px; margin-bottom: -1.25em; }
    .search { flex: 1 1 260px; }
    .list { overflow: hidden; }
  `,
})
export class TaskListComponent implements OnInit {
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly statuses = STATUSES;
  readonly priorities: Priority[] = ['Critical', 'High', 'Normal', 'Low'];
  readonly priorityOptions = enumOptions(this.priorities);

  /** The status group being looked at. A signal because the grid's shape is derived from it. */
  readonly view = signal<string | null>(null);

  /**
   * The filter row, replacing a card that held a search box, a priority dropdown, a client dropdown
   * and an "open only" toggle. Every one of them described a column in the grid below.
   *
   * "Open only" is the exception and is simply gone: the status tiles already answer it, and a view
   * like "To Do" cannot contain closed work anyway.
   */
  readonly filters = columnFilters(() => { this.pageIndex = 0; this.reload(); });

  /**
   * Which values each column can still be narrowed by, from the server. Empty until the first
   * answer arrives, and `available` is left undefined until then so nothing is hidden prematurely.
   */
  readonly options = signal<Record<string, string[]> | null>(null);

  private availableFor(key: string): ReadonlySet<string> | undefined {
    const all = this.options();
    return all?.[key] ? new Set(all[key]) : undefined;
  }

  readonly specs = computed<Record<string, ColumnFilterSpec>>(() => ({
    number: { key: 'number', kind: 'text', placeholder: 'TSK-…' },
    title: { key: 'title', kind: 'text', placeholder: 'Title', minWidth: 260 },
    client: {
      key: 'client', kind: 'select', placeholder: 'Any client',
      options: this.clients().map((c) => ({ value: c.id, label: c.name })),
      available: this.availableFor('client'),
    },
    priority: {
      key: 'priority', kind: 'select', placeholder: 'Any',
      options: this.priorities.map((p) => ({ value: p, label: p })),
      available: this.availableFor('priority'),
    },
    // "-" means nobody: unassigned work is what a coordinator scans this column for, and it has no
    // name to type.
    assignee: { key: 'assignee', kind: 'text', placeholder: 'Name, or -' },
    due: { key: 'due', kind: 'date' },
  }));

  /** The row the drawer is showing, or null when it is closed. */
  readonly peeking = signal<QuickViewTarget | null>(null);

  peek(task: { id: number }): void {
    this.peeking.set({ kind: 'task', id: task.id });
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
  readonly page = signal<PagedResult<TaskSummaryDto>>(
    { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });

  private pageIndex = 0;
  private pageSize = 25;

  label = (value: string) => priorityLabel(value as Priority);

  ngOnInit(): void {
    // A view in the URL wins over the default, so a bookmarked or shared queue opens on it.
    const view = this.route.snapshot.queryParamMap.get('view');
    if (view) this.view.set(view);

    this.api.clients().subscribe((c) => this.clients.set(c));
    this.reload();
    // Re-fetch on the server's say-so; see syncOn for why it debounces and tears down.
    syncOn(
      [this.realtime.taskChanged],
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
  readonly clientOptions = computed(() =>
    this.clients().map((c) => ({ value: c.id, label: c.name })));

  readonly totalAcross = computed(() =>
    this.counts().reduce((sum, c) => sum + c.count, 0));

  /**
   * The columns and the row action follow the view. See `list-views.ts` for why the grid is not
   * one fixed shape.
   */
  readonly grid = computed(() => taskView(this.view()));

  /**
   * A tile is the filter; clicking the active one clears it. The choice goes into the URL so the
   * browser's Back button walks back through the views the way it walks back through pages, and
   * so a particular queue can be linked to.
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

  /** The row's primary action, which depends on which queue is being looked at. */
  act(task: TaskSummaryDto): void {
    const label = this.grid().action?.label;

    if (label === 'Assign') {
      void this.router.navigate(['/assignment'], { queryParams: { task: task.id } });
      return;
    }

    // "Start work" and "Start fixing" both mean "go and work on this", which is the task screen's
    // job: it owns the timer, the single-active-session rule and the blocked check.
    void this.router.navigate(['/tasks', task.id], {
      queryParams: label?.startsWith('Start') ? { start: 1 } : {},
    });
  }

  /**
   * What each dropdown should still offer. Asked for on every reload because the answer depends on
   * the other columns — that is the whole point of it.
   */
  private loadFilterOptions(): void {
    this.api.taskFilterOptions({
      view: this.view() ?? undefined,
      openOnly: true,
      ...this.filters.asObject(),
    }).subscribe({
      next: (o) => this.options.set(o.columns),
      // Failing to narrow the choices is not worth breaking the grid over: leave them all offered.
      error: () => this.options.set(null),
    });
  }

  /** Counts ignore the filter row — see the request grid for why a tile must not move as you type. */
  private loadCounts(): void {
    this.api.taskStatusCounts({ openOnly: true }).subscribe((c) => this.counts.set(c));
  }


  /** One place to leave a load, whether it succeeded or not. */
  private settle(): void {
    this.loaded = true;
    this.loading.set(false);
    this.refreshing.set(false);
  }

  reload(): void {
    this.loadCounts();
    this.loadFilterOptions();
    if (this.loaded) this.refreshing.set(true); else this.loading.set(true);
    this.api.tasks({
      view: this.view() ?? undefined,
      openOnly: true,
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

  open(task: TaskSummaryDto): void {
    void this.router.navigate(['/tasks', task.id]);
  }
}
