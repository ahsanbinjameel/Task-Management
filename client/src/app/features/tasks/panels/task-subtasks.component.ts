import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ApiService } from '../../../core/api.service';
import { AuthService } from '../../../core/auth.service';
import { ToastService } from '../../../core/toast.service';
import { Perm } from '../../../core/permissions';
import { AssignableUserDto, TaskDetailDto, TaskSummaryDto } from '../../../core/models';
import { EmptyComponent } from '../../../shared/ui';
import { TaskTableComponent } from '../../../shared/task-table.component';

@Component({
  selector: 'app-task-subtasks',
  standalone: true,
  imports: [
    FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatSelectModule, EmptyComponent, TaskTableComponent,
  ],
  template: `
    <div class="stack">
      <div class="card">
        @if (subtasks().length === 0) {
          <app-empty message="No subtasks" icon="account_tree"
                     hint="Break the work down when it is too big to track as one item." />
        } @else {
          <app-task-table [tasks]="subtasks()"
                          [columns]="['number', 'title', 'status', 'assignee', 'worked']" />
        }
      </div>

      @if (canCreate && !isTerminal()) {
        <div class="card card-pad">
          <h2 class="card-title">Add a subtask</h2>
          <p class="muted small note">
            A subtask is a task in its own right — its own number, assignee and timer. This task
            cannot close while one is still open.
          </p>

          <mat-form-field class="full">
            <mat-label>Title</mat-label>
            <input matInput [(ngModel)]="title" />
          </mat-form-field>

          <mat-form-field class="full">
            <mat-label>Description</mat-label>
            <textarea matInput rows="2" [(ngModel)]="description"></textarea>
          </mat-form-field>

          <div class="row row-wrap">
            <mat-form-field class="grow">
              <mat-label>Assign to (optional)</mat-label>
              <mat-select [(ngModel)]="assigneeUserId">
                <mat-option [value]="null">Leave in the queue</mat-option>
                @for (user of users(); track user.id) {
                  <mat-option [value]="user.id">{{ user.displayName }}</mat-option>
                }
              </mat-select>
            </mat-form-field>

            <mat-form-field class="hours">
              <mat-label>Estimate (h)</mat-label>
              <input matInput type="number" min="0" [(ngModel)]="estimate" />
            </mat-form-field>

            <span class="spacer"></span>
            <button matButton="filled" [disabled]="!valid() || busy()" (click)="create()">
              <mat-icon>add</mat-icon> Create subtask
            </button>
          </div>
        </div>
      }
    </div>
  `,
  styles: `
    .full { width: 100%; }
    .grow { flex: 1 1 220px; margin-bottom: -1.25em; }
    .hours { width: 130px; margin-bottom: -1.25em; }
    .note { margin: -6px 0 12px; }
  `,
})
export class TaskSubtasksComponent implements OnInit {
  readonly task = input.required<TaskDetailDto>();
  readonly changed = output<void>();

  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly subtasks = signal<TaskSummaryDto[]>([]);
  readonly users = signal<AssignableUserDto[]>([]);
  readonly busy = signal(false);

  readonly canCreate = this.auth.has(Perm.taskAssign);

  title = '';
  description = '';
  assigneeUserId: number | null = null;
  estimate: number | null = null;

  ngOnInit(): void {
    this.load();
    if (this.canCreate) {
      this.api.assignableUsers().subscribe((u) => this.users.set(u));
    }
  }

  load(): void {
    this.api.subtasks(this.task().id).subscribe((page) => this.subtasks.set(page.items));
  }

  isTerminal = () => ['Closed', 'Cancelled', 'Duplicate'].includes(this.task().status);
  valid = () => this.title.trim().length > 0 && this.description.trim().length > 0;

  create(): void {
    this.busy.set(true);
    this.api.createSubtask(this.task().id, {
      title: this.title.trim(),
      description: this.description.trim(),
      assigneeUserId: this.assigneeUserId,
      estimatedEffortHours: this.estimate ?? undefined,
    }).subscribe({
      next: (created) => {
        this.busy.set(false);
        this.title = '';
        this.description = '';
        this.assigneeUserId = null;
        this.estimate = null;
        this.toast.success(`Created ${created.taskNumber}.`);
        this.load();
        this.changed.emit();
      },
      error: () => this.busy.set(false),
    });
  }
}
