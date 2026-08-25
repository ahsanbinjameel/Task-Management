import { Component, inject, input, output, computed } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { SortHeaderComponent, SortState } from './sort-header.component';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatButtonModule } from '@angular/material/button';
import { TaskSummaryDto } from '../core/models';
import { DurationPipe, sinceLabel } from '../core/format';
import { ChipComponent } from './ui';
import {
  ColumnFilterComponent, ColumnFilterSpec, ColumnFilterState,
} from './column-filter.component';

/**
 * The task list, shared by every queue screen. Columns are opt-in so the assignment queue can drop
 * "assignee" (nobody has one yet) and my-queue can drop it too (it is always me) without four
 * near-identical tables drifting apart.
 */
@Component({
  selector: 'app-task-table',
  standalone: true,
  imports: [
    RouterLink, DatePipe, MatTableModule, MatIconModule, MatTooltipModule, MatButtonModule,
    ColumnFilterComponent,
    ChipComponent,
    SortHeaderComponent,
    DurationPipe,
  ],
  template: `
    <div class="table-scroll">
      <table mat-table [dataSource]="tasks()">
        <ng-container matColumnDef="number">
          <th mat-header-cell *matHeaderCellDef>
            @if (sortable()) {
              <app-sort-header label="Task" column="number" [sort]="sort()"
                               (sortChange)="sortChange.emit($event)" />
            } @else { Task }
          </th>
          <td mat-cell *matCellDef="let t">
            <a class="number mono" [routerLink]="['/tasks', t.id]">{{ t.taskNumber }}</a>
          </td>
        </ng-container>

        <ng-container matColumnDef="title">
          <th mat-header-cell *matHeaderCellDef>
            @if (sortable()) {
              <app-sort-header label="Title" column="title" [sort]="sort()"
                               (sortChange)="sortChange.emit($event)" />
            } @else { Title }
          </th>
          <td mat-cell *matCellDef="let t">
            <a class="title" [routerLink]="['/tasks', t.id]">{{ t.title }}</a>
          </td>
        </ng-container>

        <ng-container matColumnDef="status">
          <th mat-header-cell *matHeaderCellDef>
            @if (sortable()) {
              <app-sort-header label="Status" column="status" [sort]="sort()"
                               (sortChange)="sortChange.emit($event)" />
            } @else { Status }
          </th>
          <td mat-cell *matCellDef="let t">
            <div class="row" style="gap:6px">
              <app-chip [value]="t.status" kind="status" />
              @if (t.hasActiveSession) {
                <mat-icon class="running" matTooltip="Timer running">bolt</mat-icon>
              }
            </div>
          </td>
        </ng-container>

        <ng-container matColumnDef="priority">
          <th mat-header-cell *matHeaderCellDef>
            @if (sortable()) {
              <app-sort-header label="Priority" column="priority" [sort]="sort()"
                               (sortChange)="sortChange.emit($event)" />
            } @else { Priority }
          </th>
          <td mat-cell *matCellDef="let t"><app-chip [value]="t.priority" kind="priority" /></td>
        </ng-container>

        <ng-container matColumnDef="client">
          <th mat-header-cell *matHeaderCellDef>
            @if (sortable()) {
              <app-sort-header label="Client" column="client" [sort]="sort()"
                               (sortChange)="sortChange.emit($event)" />
            } @else { Client }
          </th>
          <td mat-cell *matCellDef="let t">
            @if (t.clientName) {
              <span class="truncate">{{ t.clientName }}</span>
            } @else {
              <span class="muted small">Internal</span>
            }
          </td>
        </ng-container>

        <ng-container matColumnDef="assignee">
          <th mat-header-cell *matHeaderCellDef>
            @if (sortable()) {
              <app-sort-header label="Responsible" column="assignee" [sort]="sort()"
                               (sortChange)="sortChange.emit($event)" />
            } @else { Responsible }
          </th>
          <td mat-cell *matCellDef="let t">
            @if (t.primaryAssigneeDisplayName) {
              <span class="truncate">{{ t.primaryAssigneeDisplayName }}</span>
            } @else {
              <span class="muted small">Nobody yet</span>
            }
          </td>
        </ng-container>

        <ng-container matColumnDef="due">
          <th mat-header-cell *matHeaderCellDef>
            @if (sortable()) {
              <app-sort-header label="Due" column="due" [sort]="sort()"
                               (sortChange)="sortChange.emit($event)" />
            } @else { Due }
          </th>
          <td mat-cell *matCellDef="let t">
            @if (t.dueDate) {
              <span class="nowrap" [class.overdue]="isOverdue(t)">{{ t.dueDate | date: 'MMM d' }}</span>
            } @else { <span class="muted">—</span> }
          </td>
        </ng-container>

        <ng-container matColumnDef="worked">
          <th mat-header-cell *matHeaderCellDef>Worked</th>
          <td mat-cell *matCellDef="let t" class="mono">{{ t.totalWorkedTime | duration }}</td>
        </ng-container>

        <ng-container matColumnDef="progress">
          <th mat-header-cell *matHeaderCellDef>Progress</th>
          <td mat-cell *matCellDef="let t">
            <div class="progress" [matTooltip]="t.progressPercent + '%'">
              <i [style.width.%]="t.progressPercent"></i>
            </div>
          </td>
        </ng-container>

        <!--
          Dates. Each view picks the ones it needs, so nobody reads a column of dashes: a queue
          nobody has started has no "started", and finished work has no "due".
        -->
        <ng-container matColumnDef="waitingSince">
          <th mat-header-cell *matHeaderCellDef>Waiting since</th>
          <td mat-cell *matCellDef="let t">{{ since(t.statusSince) }}</td>
        </ng-container>

        <ng-container matColumnDef="statusSince">
          <th mat-header-cell *matHeaderCellDef>Since</th>
          <td mat-cell *matCellDef="let t">{{ since(t.statusSince) }}</td>
        </ng-container>

        <ng-container matColumnDef="assignedAt">
          <th mat-header-cell *matHeaderCellDef>Assigned</th>
          <td mat-cell *matCellDef="let t">
            @if (t.assignedAt) {
              <span class="nowrap">{{ t.assignedAt | date: 'MMM d' }}</span>
            } @else { <span class="muted">—</span> }
          </td>
        </ng-container>

        <ng-container matColumnDef="startedAt">
          <th mat-header-cell *matHeaderCellDef>Started</th>
          <td mat-cell *matCellDef="let t">
            @if (t.startedAt) {
              <span class="nowrap">{{ t.startedAt | date: 'MMM d' }}</span>
            } @else { <span class="muted">Not yet</span> }
          </td>
        </ng-container>

        <ng-container matColumnDef="completedAt">
          <th mat-header-cell *matHeaderCellDef>Completed</th>
          <td mat-cell *matCellDef="let t">
            @if (t.completedAt) {
              <span class="nowrap">{{ t.completedAt | date: 'MMM d' }}</span>
            } @else { <span class="muted">—</span> }
          </td>
        </ng-container>

        <ng-container matColumnDef="requestedBy">
          <th mat-header-cell *matHeaderCellDef>Requested by</th>
          <td mat-cell *matCellDef="let t">
            @if (t.requestedByDisplayName) {
              <span class="truncate">{{ t.requestedByDisplayName }}</span>
            } @else { <span class="muted">—</span> }
          </td>
        </ng-container>

        <ng-container matColumnDef="estimate">
          <th mat-header-cell *matHeaderCellDef>Estimate</th>
          <td mat-cell *matCellDef="let t" class="mono">
            @if (t.estimatedEffortHours) { {{ t.estimatedEffortHours }}h }
            @else { <span class="muted">—</span> }
          </td>
        </ng-container>

        <!-- Why it stopped. Pause, block and hold all had to give a reason to get here. -->
        <ng-container matColumnDef="reason">
          <th mat-header-cell *matHeaderCellDef>Reason</th>
          <td mat-cell *matCellDef="let t">
            @if (t.statusReason) {
              <span class="truncate" [matTooltip]="t.statusReason">{{ t.statusReason }}</span>
            } @else { <span class="muted">Not given</span> }
          </td>
        </ng-container>

        <ng-container matColumnDef="checker">
          <th mat-header-cell *matHeaderCellDef>Quality checker</th>
          <td mat-cell *matCellDef="let t">
            @if (t.qcUserDisplayName) {
              <span class="truncate">{{ t.qcUserDisplayName }}</span>
            } @else { <span class="muted small">Anyone</span> }
          </td>
        </ng-container>

        <ng-container matColumnDef="checkedBy">
          <th mat-header-cell *matHeaderCellDef>Checked by</th>
          <td mat-cell *matCellDef="let t">
            @if (t.checkedByDisplayName) {
              <span class="truncate">{{ t.checkedByDisplayName }}</span>
            } @else { <span class="muted">—</span> }
          </td>
        </ng-container>

        <ng-container matColumnDef="checkedAt">
          <th mat-header-cell *matHeaderCellDef>Checked</th>
          <td mat-cell *matCellDef="let t">
            @if (t.checkedAt) {
              <span class="nowrap">{{ t.checkedAt | date: 'MMM d' }}</span>
            } @else { <span class="muted">—</span> }
          </td>
        </ng-container>

        <ng-container matColumnDef="checkNotes">
          <th mat-header-cell *matHeaderCellDef>What came back</th>
          <td mat-cell *matCellDef="let t">
            @if (t.checkNotes) {
              <span class="truncate wide" [matTooltip]="t.checkNotes">{{ t.checkNotes }}</span>
            } @else { <span class="muted small">No note</span> }
          </td>
        </ng-container>

        <ng-container matColumnDef="action">
          <th mat-header-cell *matHeaderCellDef aria-label="Actions"></th>
          <td mat-cell *matCellDef="let t" class="action-cell">
            <ng-content />
            @if (!actionWhen() || actionWhen()!(t)) {
              <button class="action" type="button" (click)="action.emit(t); $event.stopPropagation()">
                {{ actionLabel() }}
              </button>
            }
          </td>
        </ng-container>

        <!--
          A look without leaving the list.

          Its own column, appended here rather than named in list-views: the action column only
          exists for views that define an action, so hanging the trigger off that one made it
          vanish on every view that does not. Hidden below 1100px, where there is no room for a
          panel beside a list and the full page is the better answer anyway.
        -->
        <ng-container matColumnDef="preview">
          <th mat-header-cell *matHeaderCellDef aria-label="Quick look"></th>
          <td mat-cell *matCellDef="let t" class="peek-cell">
            <button matIconButton class="peek" type="button" matTooltip="Quick look"
                    [attr.aria-label]="'Quick look at ' + t.taskNumber"
                    (click)="preview.emit(t); $event.stopPropagation()">
              <mat-icon>visibility</mat-icon>
            </button>
          </td>
        </ng-container>

        <!--
          The filter row, generated from whatever columns this view asked for. Present only when a
          parent supplies filter state — the QC queue and the assignment queue are short, already
          scoped lists where a filter row would be chrome over four rows.
        -->
        @if (filters(); as state) {
          @for (column of renderedColumns(); track column) {
            <ng-container [matColumnDef]="column + '_filter'">
              <th mat-header-cell *matHeaderCellDef class="filter-cell">
                <app-column-filter [spec]="specs()[column]" [value]="state.value(column)"
                                   (changed)="state.set(specs()[column], column, $event)" />
              </th>
            </ng-container>
          }
        }

        <tr mat-header-row *matHeaderRowDef="renderedColumns()"></tr>
        @if (filters()) {
          <tr mat-header-row *matHeaderRowDef="filterRow()" class="filter-row"></tr>
        }
        <tr mat-row *matRowDef="let row; columns: renderedColumns()"
            class="clickable" tabindex="0"
            (click)="open(row)" (keydown.enter)="open(row)"></tr>
      </table>
    </div>
  `,
  styles: `
    .number { font-size: 12.5px; color: var(--text-muted); text-decoration: none; }
    .number:hover { text-decoration: underline; }
    .title {
      color: inherit; text-decoration: none; font-weight: 500;
      display: inline-block; max-width: 420px; overflow: hidden;
      text-overflow: ellipsis; white-space: nowrap; vertical-align: bottom;
    }
    .title:hover { text-decoration: underline; }
    .running { color: var(--tone-running-fg); font-size: 17px; width: 17px; height: 17px; }
    .progress { width: 74px; height: 6px; border-radius: 999px; background: var(--surface-sunken); }
    .progress i { display: block; height: 100%; border-radius: 999px; background: #1d69d4; }
    .action-cell { text-align: right; white-space: nowrap; }
    .peek-cell { width: 44px; padding-right: 4px !important; }
    .peek { vertical-align: middle; }
    .peek mat-icon { font-size: 18px; width: 18px; height: 18px; color: var(--text-muted); }
    @media (max-width: 1100px) { .peek-cell, .peek { display: none; } }
    tr.clickable { cursor: pointer; }
    tr.clickable:hover { background: var(--surface-sunken); }
    tr.clickable:focus-visible { outline: 2px solid #1d69d4; outline-offset: -2px; }
    .truncate.wide { max-width: 320px; }
    .action {
      border: 1px solid var(--border-strong); background: var(--surface);
      border-radius: 7px; padding: 4px 12px; font: inherit; font-size: 12.5px;
      font-weight: 500; cursor: pointer;
    }
    .action:hover { background: var(--surface-sunken); }
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
  readonly specs = input<Record<string, ColumnFilterSpec>>({});

  /** Whatever the view asked for, plus the quick-look column when it is switched on. */
  readonly renderedColumns = computed(() =>
    this.showPreview() ? [...this.columns(), 'preview'] : this.columns());

  /** One filter cell per rendered column, so the two header rows always line up. */
  readonly filterRow = computed(() => this.renderedColumns().map((c) => c + '_filter'));

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

  /** Shared with the home screen — see `sinceLabel`. */
  readonly since = sinceLabel;
}
