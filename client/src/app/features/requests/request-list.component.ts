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
import { PagedResult, RequestStatus, RequestSummaryDto , ClientOptionDto, StatusCountDto} from '../../core/models';
import { humanizeEnum } from '../../core/format';
import { SearchSelectComponent } from '../../shared/search-select.component';
import { ChipComponent, EmptyComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';

const STATUSES: RequestStatus[] = [
  'Submitted', 'InReview', 'ClarificationRequired', 'Approved', 'Rejected', 'Duplicate',
  'Deferred', 'Escalated',
];

@Component({
  selector: 'app-request-list',
  standalone: true,
  imports: [
    DatePipe, FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatSlideToggleModule, MatTableModule, MatPaginatorModule,
    MatTooltipModule, PageHeaderComponent, ChipComponent, EmptyComponent, LoadingComponent,
    StatusTilesComponent, SortHeaderComponent, SearchSelectComponent,
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

      <div class="card card-pad filters">
        <mat-form-field class="search">
          <mat-label>Search</mat-label>
          <input matInput [(ngModel)]="search" (keyup.enter)="reload()" />
          <mat-icon matSuffix>search</mat-icon>
        </mat-form-field>

        <app-search-select label="Client" nullLabel="Any" [options]="clientOptions()"
                           [(ngModel)]="clientId" (valueChange)="reload()" />

        @if (auth.has(Perm.requestViewAll)) {
          <mat-slide-toggle [(ngModel)]="mine" (change)="reload()">Only mine</mat-slide-toggle>
        }
        <span class="spacer"></span>
        <button matButton (click)="reload()"><mat-icon>refresh</mat-icon> Refresh</button>
      </div>

      <div class="card">
        @if (loading()) {
          <app-loading />
        } @else if (page().items.length === 0) {
          <app-empty message="No requests found" icon="inbox" />
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
                  <app-chip [value]="r.requestedUrgency" kind="priority" />
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

              <tr mat-header-row *matHeaderRowDef="columns()"></tr>
              <tr mat-row *matRowDef="let row; columns: columns()"
                  class="clickable" tabindex="0"
                  (click)="open(row)" (keydown.enter)="open(row)"></tr>
            </table>
          </div>
          <mat-paginator [length]="page().totalCount" [pageSize]="page().pageSize"
                         [pageIndex]="page().page - 1" [pageSizeOptions]="[25, 50]"
                         (page)="onPage($event)" />
        }
      </div>
    </div>
  `,
  styles: `
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

  search = '';
  readonly view = signal<string | null>(null);
  clientId: number | null = null;
  mine = false;

  readonly loading = signal(true);
  readonly page = signal<PagedResult<RequestSummaryDto>>(
    { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });

  private pageIndex = 0;
  private pageSize = 25;

  label = (value: string) => humanizeEnum(value);

  ngOnInit(): void {
    const view = this.route.snapshot.queryParamMap.get('view');
    if (view) this.view.set(view);

    // Someone without ViewAll only ever sees their own; no point offering the toggle.
    this.mine = !this.auth.has(Perm.requestViewAll);
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
  readonly clientOptions = computed(() =>
    this.clients().map((c) => ({ value: c.id, label: c.name })));

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

  private loadCounts(): void {
    this.api.requestStatusCounts({
      clientId: this.clientId ?? undefined,
      search: this.search || undefined,
      mine: this.mine || undefined,
    }).subscribe((c) => this.counts.set(c));
  }

  reload(): void {
    this.loadCounts();
    this.loading.set(true);
    this.api.requests({
      search: this.search || undefined,
      view: this.view() ?? undefined,
      clientId: this.clientId ?? undefined,
      sortBy: this.sort().by ?? undefined,
      sortDescending: this.sort().descending,
      mine: this.mine || undefined,
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
}
