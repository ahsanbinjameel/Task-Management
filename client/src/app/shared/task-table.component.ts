import { Component, computed, inject, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { PageEvent } from '@angular/material/paginator';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatButtonModule } from '@angular/material/button';
import { SortState } from './sort-header.component';
import { TaskSummaryDto } from '../core/models';
import { DurationPipe, parseTimeSpan, sinceLabel } from '../core/format';
import { ChipComponent } from './ui';
import { ColumnFilterSpec, ColumnFilterState } from './column-filter.component';
import { DataGridComponent, GridCellDirective, GridColumn } from './data-grid.component';

/**
 * The catalogue of task columns: what each one is called, whether it sorts, and what it reads.
 *
 * Separate from the markup because the choice of columns is a *view* decision (see `list-views.ts`)
 * while the definition of a column is a fact about a task. A view names the keys it wants; this
 * says what those keys mean. Rendering is `app-data-grid`'s job, and cells that need more than
 * text are declared as `gridCell` templates below.
 */
const COLUMNS: Record<string, GridColumn<TaskSummaryDto>> = {
  number: { key: 'number', header: 'Task', sortable: true, cell: (t) => t.taskNumber },
  title: { key: 'title', header: 'Title', sortable: true, cell: (t) => t.title, minWidth: 260 },
  status: { key: 'status', header: 'Status', sortable: true, cell: (t) => t.status },
  priority: { key: 'priority', header: 'Priority', sortable: true, cell: (t) => t.priority },
  client: { key: 'client', header: 'Client', sortable: true, cell: (t) => t.clientName },
  assignee: {
    key: 'assignee', header: 'Responsible', sortable: true,
    cell: (t) => t.primaryAssigneeDisplayName,
  },
  due: { key: 'due', header: 'Due', sortable: true, filterValue: (t) => t.dueDate },
  worked: {
    key: 'worked', header: 'Worked', cellClass: 'mono',
    sortValue: (t) => parseTimeSpan(t.totalWorkedTime),
  },
  progress: { key: 'progress', header: 'Progress', sortValue: (t) => t.progressPercent },
  waitingSince: { key: 'waitingSince', header: 'Waiting since', cell: (t) => sinceLabel(t.statusSince) },
  statusSince: { key: 'statusSince', header: 'Since', cell: (t) => sinceLabel(t.statusSince) },
  assignedAt: { key: 'assignedAt', header: 'Assigned', filterValue: (t) => t.assignedAt },
  startedAt: { key: 'startedAt', header: 'Started', filterValue: (t) => t.startedAt },
  completedAt: { key: 'completedAt', header: 'Completed', filterValue: (t) => t.completedAt },
  requestedBy: { key: 'requestedBy', header: 'Requested by', cell: (t) => t.requestedByDisplayName },
  estimate: { key: 'estimate', header: 'Estimate', cellClass: 'mono' },
  reason: { key: 'reason', header: 'Reason', cell: (t) => t.statusReason },
  checker: { key: 'checker', header: 'Quality checker', cell: (t) => t.qcUserDisplayName },
  checkedBy: { key: 'checkedBy', header: 'Checked by', cell: (t) => t.checkedByDisplayName },
  checkedAt: { key: 'checkedAt', header: 'Checked', filterValue: (t) => t.checkedAt },
  checkNotes: { key: 'checkNotes', header: 'What came back', cell: (t) => t.checkNotes },
  action: { key: 'action', header: 'Actions', headerHidden: true, align: 'right' },
  preview: { key: 'preview', header: 'Quick look', headerHidden: true, align: 'right' },
};

/**
 * The task list, shared by every queue screen.
 *
 * A thin layer over `app-data-grid`: it knows what a task column is and nothing else. The grid
 * owns the table, the filter row, the sorting, the empty states and the paginator, so the four
 * screens that show tasks cannot drift apart in any of those.
 */
@Component({
  selector: 'app-task-table',
  standalone: true,
  imports: [
    RouterLink, DatePipe, MatIconModule, MatTooltipModule, MatButtonModule,
    ChipComponent, DurationPipe, DataGridComponent, GridCellDirective,
  ],
  template: `
    <app-data-grid
      [rows]="tasks()" [columns]="grid()"
      [loading]="loading()" [refreshing]="refreshing()"
      [filters]="filters()" [filterOptions]="filterOptions()" [externalFilter]="externalFilter()"
      [sort]="sort()" (sortChange)="sortChange.emit($event)"
      [total]="total()" [pageSize]="pageSize()" [pageIndex]="pageIndex()"
      [pageSizeOptions]="pageSizeOptions()" (pageChange)="pageChange.emit($event)"
      [emptyMessage]="emptyMessage()" [emptyIcon]="emptyIcon()"
      noMatchesMessage="No work matches those filters."
      [clickable]="true" (rowClick)="open($event)" (clearedFilters)="clearedFilters.emit()">

      <ng-template gridCell="number" let-t>
        <a class="grid-link mono" [routerLink]="['/tasks', t.id]">{{ t.taskNumber }}</a>
      </ng-template>

      <ng-template gridCell="title" let-t>
        <a class="grid-title" [routerLink]="['/tasks', t.id]">{{ t.title }}</a>
      </ng-template>

      <ng-template gridCell="status" let-t>
        <div class="row" style="gap:6px">
          <app-chip [value]="t.status" kind="status" />
          @if (t.hasActiveSession) {
            <mat-icon class="running" matTooltip="Timer running">bolt</mat-icon>
          }
        </div>
      </ng-template>

      <ng-template gridCell="priority" let-t>
        <app-chip [value]="t.priority" kind="priority" />
      </ng-template>

      <ng-template gridCell="client" let-t>
        @if (t.clientName) {
          <span class="truncate">{{ t.clientName }}</span>
        } @else { <span class="muted small">Internal</span> }
      </ng-template>

      <ng-template gridCell="assignee" let-t>
        @if (t.primaryAssigneeDisplayName) {
          <span class="truncate">{{ t.primaryAssigneeDisplayName }}</span>
        } @else { <span class="muted small">Nobody yet</span> }
      </ng-template>

      <ng-template gridCell="due" let-t>
        @if (t.dueDate) {
          <span class="nowrap" [class.overdue]="isOverdue(t)">{{ t.dueDate | date: 'MMM d' }}</span>
        } @else { <span class="muted">—</span> }
      </ng-template>

      <ng-template gridCell="worked" let-t>{{ t.totalWorkedTime | duration }}</ng-template>

      <ng-template gridCell="progress" let-t>
        <div class="grid-progress" [matTooltip]="t.progressPercent + '%'">
          <i [style.width.%]="t.progressPercent"></i>
        </div>
      </ng-template>

      <ng-template gridCell="assignedAt" let-t>
        @if (t.assignedAt) {
          <span class="nowrap">{{ t.assignedAt | date: 'MMM d' }}</span>
        } @else { <span class="muted">—</span> }
      </ng-template>

      <ng-template gridCell="startedAt" let-t>
        @if (t.startedAt) {
          <span class="nowrap">{{ t.startedAt | date: 'MMM d' }}</span>
        } @else { <span class="muted">Not yet</span> }
      </ng-template>

      <ng-template gridCell="completedAt" let-t>
        @if (t.completedAt) {
          <span class="nowrap">{{ t.completedAt | date: 'MMM d' }}</span>
        } @else { <span class="muted">—</span> }
      </ng-template>

      <ng-template gridCell="requestedBy" let-t>
        @if (t.requestedByDisplayName) {
          <span class="truncate">{{ t.requestedByDisplayName }}</span>
        } @else { <span class="muted">—</span> }
      </ng-template>

      <ng-template gridCell="estimate" let-t>
        @if (t.estimatedEffortHours) { {{ t.estimatedEffortHours }}h }
        @else { <span class="muted">—</span> }
      </ng-template>

      <!-- Why it stopped. Pause, block and hold all had to give a reason to get here. -->
      <ng-template gridCell="reason" let-t>
        @if (t.statusReason) {
          <span class="truncate wide" [matTooltip]="t.statusReason">{{ t.statusReason }}</span>
        } @else { <span class="muted">Not given</span> }
      </ng-template>

      <ng-template gridCell="checker" let-t>
        @if (t.qcUserDisplayName) {
          <span class="truncate">{{ t.qcUserDisplayName }}</span>
        } @else { <span class="muted small">Anyone</span> }
      </ng-template>

      <ng-template gridCell="checkedBy" let-t>
        @if (t.checkedByDisplayName) {
          <span class="truncate">{{ t.checkedByDisplayName }}</span>
        } @else { <span class="muted">—</span> }
      </ng-template>

      <ng-template gridCell="checkedAt" let-t>
        @if (t.checkedAt) {
          <span class="nowrap">{{ t.checkedAt | date: 'MMM d' }}</span>
        } @else { <span class="muted">—</span> }
      </ng-template>

      <ng-template gridCell="checkNotes" let-t>
        @if (t.checkNotes) {
          <span class="truncate wide" [matTooltip]="t.checkNotes">{{ t.checkNotes }}</span>
        } @else { <span class="muted small">No note</span> }
      </ng-template>

      <ng-template gridCell="action" let-t>
        @if (!actionWhen() || actionWhen()!(t)) {
          <button class="grid-action" type="button"
                  (click)="action.emit(t); $event.stopPropagation()">{{ actionLabel() }}</button>
        }
      </ng-template>

      <ng-template gridCell="preview" let-t>
        <button matIconButton class="grid-peek" type="button" matTooltip="Quick look"
                [attr.aria-label]="'Quick look at ' + t.taskNumber"
                (click)="preview.emit(t); $event.stopPropagation()">
          <mat-icon>visibility</mat-icon>
        </button>
      </ng-template>
    </app-data-grid>
  `,
  styles: `
    .running { color: var(--tone-running-fg); font-size: 17px; width: 17px; height: 17px; }
    .truncate.wide { max-width: 320px; }
  `,
})
export class TaskTableComponent {
  readonly tasks = input.required<TaskSummaryDto[]>();

  /** Headings become clickable only where the parent actually handles sorting. */
  readonly sortable = input(false);
  readonly sort = input<SortState>({ by: null, descending: true });
  readonly sortChange = output<SortState>();

  readonly columns = input<string[]>([
    'number', 'title', 'client', 'status', 'priority', 'assignee', 'due', 'worked',
  ]);
  readonly actionLabel = input('Open');
  /** Offers the action only where it makes sense — no "Start work" on work already running. */
  readonly actionWhen = input<((task: TaskSummaryDto) => boolean) | null>(null);
  readonly action = output<TaskSummaryDto>();

  /**
   * Opens the quick-view drawer. Asked for explicitly rather than inferred from whether anyone is
   * listening: `OutputEmitterRef` has no subscriber count, and a control that appears depending on
   * how a parent happens to be wired is one nobody can reason about.
   */
  readonly showPreview = input(false);
  readonly preview = output<TaskSummaryDto>();

  /**
   * The filter row's state, owned by the parent because it is the parent that reloads.
   * Left null and the row is not rendered at all.
   */
  readonly filters = input<ColumnFilterState | null>(null);
  /** Per-column filter controls, supplied by the parent because some options are fetched. */
  readonly specs = input<Record<string, ColumnFilterSpec>>({});
  readonly filterOptions = input<Record<string, string[]> | null>(null);
  readonly externalFilter = input(false);

  readonly loading = input(false);
  readonly refreshing = input(false);
  readonly emptyMessage = input('No work here');
  readonly emptyIcon = input('search_off');

  readonly total = input<number | null>(null);
  readonly pageSize = input(25);
  readonly pageIndex = input(0);
  readonly pageSizeOptions = input<number[]>([25, 50, 100]);
  readonly pageChange = output<PageEvent>();
  readonly clearedFilters = output<void>();

  /**
   * The view's columns, looked up in the catalogue and given whatever the parent knows that the
   * catalogue cannot — the filter control, and whether headings sort on this screen.
   */
  readonly grid = computed<GridColumn<TaskSummaryDto>[]>(() => {
    const keys = this.showPreview() ? [...this.columns(), 'preview'] : this.columns();
    const specs = this.specs();
    const sortable = this.sortable();

    return keys
      .map((key) => COLUMNS[key])
      .filter((column): column is GridColumn<TaskSummaryDto> => !!column)
      .map((column) => ({
        ...column,
        sortable: sortable && column.sortable,
        filter: specs[column.key],
      }));
  });

  /**
   * Clicking the row opens the task. Aiming at a five-character reference number is a needless
   * demand on the reader; the links inside the row still work, and the action button stops the
   * click so it never does two things at once.
   */
  private readonly router = inject(Router);

  open(task: TaskSummaryDto): void {
    void this.router.navigate(['/tasks', task.id]);
  }

  isOverdue(task: TaskSummaryDto): boolean {
    return !!task.dueDate && new Date(task.dueDate) < new Date() && task.status !== 'Closed';
  }
}
