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
    FormsModule, MatFormFieldModule, MatInputModule, MatButtonModule,
    MatIconModule, MatPaginatorModule, MatSlideToggleModule,
    PageHeaderComponent, EmptyComponent, LoadingComponent, TaskTableComponent, QuickViewComponent,
    StatusTilesComponent, SearchSelectComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Tasks" subtitle="Work you are part of, and work you oversee." />

      <app-status-tiles
        [counts]="counts()"
        [selected]="view()"
        [total]="totalAcross()"
        (pick)="pickView($event)" />

      <div class="card card-pad filters">
        <mat-form-field class="search">
          <mat-label>Search</mat-label>
          <input matInput [(ngModel)]="search" (keyup.enter)="reload()"
                 placeholder="Title or task number" />
          <mat-icon matSuffix>search</mat-icon>
        </mat-form-field>

        <app-search-select label="Priority" nullLabel="Any" [options]="priorityOptions"
                           [(ngModel)]="priority" (valueChange)="reload()" />

        <app-search-select label="Client" nullLabel="Any" [options]="clientOptions()"
                           [(ngModel)]="clientId" (valueChange)="reload()" />

        <mat-slide-toggle [(ngModel)]="openOnly" (change)="reload()">Open only</mat-slide-toggle>
        <span class="spacer"></span>
        <button matButton (click)="reload()"><mat-icon>refresh</mat-icon> Refresh</button>
      </div>

      <div class="card list">
        @if (loading()) {
          <app-loading />
        } @else if (page().items.length === 0) {
          <app-empty message="No work matches" icon="search_off"
                     hint="Clear the search box, the client filter or the status card above." />
        } @else {
          <app-task-table [tasks]="page().items" (action)="act($event)"
                          [columns]="grid().columns.concat(grid().action ? ['action'] : [])"
                          [actionLabel]="grid().action?.label ?? 'Open'"
                          [actionWhen]="grid().action?.when ?? null"
                          [sortable]="true" [sort]="sort()" (sortChange)="applySort($event)" 
                          [showPreview]="true" (preview)="peek($event)"/>
          <mat-paginator [length]="page().totalCount" [pageSize]="page().pageSize"
                         [pageIndex]="page().page - 1" [pageSizeOptions]="[25, 50, 100]"
                         (page)="onPage($event)" />
        }
      </div>

      <app-quick-view [target]="peeking()" (close)="peeking.set(null)" />
    </div>
  `,
  styles: `
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

  search = '';
  /** The status group being looked at. A signal because the grid's shape is derived from it. */
  readonly view = signal<string | null>(null);
  clientId: number | null = null;
  priority: Priority | null = null;
  openOnly = true;

  /** The row the drawer is showing, or null when it is closed. */
  readonly peeking = signal<QuickViewTarget | null>(null);

  peek(task: { id: number }): void {
    this.peeking.set({ kind: 'task', id: task.id });
  }
  readonly loading = signal(true);
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

  private loadCounts(): void {
    this.api.taskStatusCounts({
      clientId: this.clientId ?? undefined,
      search: this.search || undefined,
      openOnly: this.openOnly,
    }).subscribe((c) => this.counts.set(c));
  }

  reload(): void {
    this.loadCounts();
    this.loading.set(true);
    this.api.tasks({
      search: this.search || undefined,
      view: this.view() ?? undefined,
      priority: this.priority ?? undefined,
      clientId: this.clientId ?? undefined,
      openOnly: this.openOnly,
      sortBy: this.sort().by ?? undefined,
      sortDescending: this.sort().descending,
      page: this.pageIndex + 1,
      pageSize: this.pageSize,
    }).subscribe({
      next: (result) => { this.page.set(result); this.loading.set(false); },
      error: () => this.loading.set(false),
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
