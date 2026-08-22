import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ApiService } from '../../core/api.service';
import { AssignableUserDto, TaskDetailDto, TaskSummaryDto } from '../../core/models';
import { ChipComponent } from '../../shared/ui';

export interface AssignDialogData {
  task: TaskSummaryDto | TaskDetailDto;
  /** True when the task already has an assignee — the API then demands a reason. */
  isReassign: boolean;
  currentAssigneeId?: number | null;
  rowVersion?: string | null;
}

export interface AssignDialogResult {
  assigneeUserId: number | null;
  reason?: string;
  rowVersion?: string | null;
}

@Component({
  selector: 'app-assign-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, ChipComponent,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.isReassign ? 'Reassign' : 'Assign' }} {{ number() }}</h2>

    <mat-dialog-content>
      <mat-form-field class="full">
        <mat-label>Assignee</mat-label>
        <mat-select [(ngModel)]="assigneeUserId">
          <mat-option [value]="null">Unassign — return to the queue</mat-option>
          @for (user of users(); track user.id) {
            <mat-option [value]="user.id" [disabled]="user.id === data.currentAssigneeId">
              <span class="option">
                {{ user.displayName }}
                <app-chip [value]="user.workforceState" kind="workforce" />
              </span>
            </mat-option>
          }
        </mat-select>
        <mat-hint>Availability is live — someone on a break can still be assigned work.</mat-hint>
      </mat-form-field>

      @if (data.isReassign) {
        <mat-form-field class="full">
          <mat-label>Reason for reassigning</mat-label>
          <textarea matInput rows="3" [(ngModel)]="reason" required></textarea>
          <mat-hint>Required: reassignment is recorded on the task's history.</mat-hint>
        </mat-form-field>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>Cancel</button>
      <button matButton="filled" [disabled]="!valid()" (click)="confirm()">
        {{ data.isReassign ? 'Reassign' : 'Assign' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; }
    mat-dialog-content { min-width: min(460px, 82vw); padding-top: 8px !important; }
    .option { display: flex; align-items: center; gap: 8px; }
  `,
})
export class AssignDialogComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly ref = inject(MatDialogRef<AssignDialogComponent, AssignDialogResult>);
  readonly data = inject<AssignDialogData>(MAT_DIALOG_DATA);

  readonly users = signal<AssignableUserDto[]>([]);
  assigneeUserId: number | null = null;
  reason = '';

  number = () => 'taskNumber' in this.data.task ? this.data.task.taskNumber : '';

  ngOnInit(): void {
    this.api.assignableUsers().subscribe((users) => this.users.set(users));
  }

  valid(): boolean {
    if (this.data.isReassign && !this.reason.trim()) return false;
    return this.assigneeUserId !== this.data.currentAssigneeId;
  }

  confirm(): void {
    this.ref.close({
      assigneeUserId: this.assigneeUserId,
      reason: this.reason.trim() || undefined,
      rowVersion: this.data.rowVersion,
    });
  }
}
