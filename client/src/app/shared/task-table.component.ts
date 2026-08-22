import { Component, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TaskSummaryDto } from '../core/models';
import { DurationPipe } from '../core/format';
import { ChipComponent } from './ui';

/**
 * The task list, shared by every queue screen. Columns are opt-in so the assignment queue can drop
 * "assignee" (nobody has one yet) and my-queue can drop it too (it is always me) without four
 * near-identical tables drifting apart.
 */
@Component({
  selector: 'app-task-table',
  standalone: true,
  imports: [
    RouterLink, DatePipe, MatTableModule, MatIconModule, MatTooltipModule, ChipComponent,
    DurationPipe,
  ],
  template: `
    <div class="table-scroll">
      <table mat-table [dataSource]="tasks()">
        <ng-container matColumnDef="number">
          <th mat-header-cell *matHeaderCellDef>Task</th>
          <td mat-cell *matCellDef="let t">
            <a class="number mono" [routerLink]="['/tasks', t.id]">{{ t.taskNumber }}</a>
          </td>
        </ng-container>

        <ng-container matColumnDef="title">
          <th mat-header-cell *matHeaderCellDef>Title</th>
          <td mat-cell *matCellDef="let t">
            <a class="title" [routerLink]="['/tasks', t.id]">{{ t.title }}</a>
          </td>
        </ng-container>

        <ng-container matColumnDef="status">
          <th mat-header-cell *matHeaderCellDef>Status</th>
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
          <th mat-header-cell *matHeaderCellDef>Priority</th>
          <td mat-cell *matCellDef="let t"><app-chip [value]="t.priority" kind="priority" /></td>
        </ng-container>

        <ng-container matColumnDef="assignee">
          <th mat-header-cell *matHeaderCellDef>Assignee</th>
          <td mat-cell *matCellDef="let t">
            @if (t.primaryAssigneeDisplayName) {
              <span class="truncate">{{ t.primaryAssigneeDisplayName }}</span>
            } @else {
              <span class="muted small">Unassigned</span>
            }
          </td>
        </ng-container>

        <ng-container matColumnDef="due">
          <th mat-header-cell *matHeaderCellDef>Due</th>
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

        <ng-container matColumnDef="action">
          <th mat-header-cell *matHeaderCellDef aria-label="Actions"></th>
          <td mat-cell *matCellDef="let t" class="action-cell">
            <ng-content />
            <button class="action" type="button" (click)="action.emit(t)">
              {{ actionLabel() }}
            </button>
          </td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="columns()"></tr>
        <tr mat-row *matRowDef="let row; columns: columns()"></tr>
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
    .action-cell { text-align: right; }
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
  readonly columns = input<string[]>([
    'number', 'title', 'status', 'priority', 'assignee', 'due', 'worked',
  ]);
  readonly actionLabel = input('Open');
  readonly action = output<TaskSummaryDto>();

  isOverdue(task: TaskSummaryDto): boolean {
    return !!task.dueDate && new Date(task.dueDate) < new Date() && task.status !== 'Closed';
  }
}
