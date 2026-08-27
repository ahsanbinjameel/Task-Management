import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../../core/api.service';
import { AuthService } from '../../../core/auth.service';
import { ToastService } from '../../../core/toast.service';
import { Perm } from '../../../core/permissions';
import { AssignableUserDto, SubtaskSummaryDto, TaskDetailDto } from '../../../core/models';
import { SearchSelectComponent } from '../../../shared/search-select.component';
import { ChipComponent, EmptyComponent } from '../../../shared/ui';
import { TaskTableComponent } from '../../../shared/task-table.component';

@Component({
  selector: 'app-task-subtasks',
  standalone: true,
  imports: [
    FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatCheckboxModule, RouterLink, EmptyComponent, ChipComponent, SearchSelectComponent,
  ],
  template: `
    <div class="stack">
      <div class="card">
        <div class="card-pad head">
          <h2 class="card-title" style="margin:0">Smaller tasks</h2>
          @if (outstanding().length > 0) {
            <p class="blocked small">
              <mat-icon>info</mat-icon>
              <span>
                This task cannot be finished yet because
                {{ outstanding().length === 1
                    ? 'one smaller task still has to be done'
                    : outstanding().length + ' smaller tasks still have to be done' }}.
              </span>
            </p>
          } @else if (parentSubtasks().length > 0) {
            <p class="muted small" style="margin:4px 0 0">
              Everything that had to be done is finished.
            </p>
          }
        </div>

        @if (parentSubtasks().length === 0) {
          <app-empty message="This task has not been broken into smaller tasks" icon="account_tree" />
        } @else {
          @for (sub of parentSubtasks(); track sub.taskId) {
            <a class="sub" [routerLink]="['/tasks', sub.taskId]">
              <span class="mono small num">{{ sub.taskNumber }}</span>
              <span class="title truncate">{{ sub.title }}</span>
              <app-chip [value]="sub.status" kind="status" />
              <span class="who muted small">
                {{ sub.responsiblePersonName ?? 'Nobody yet' }}
              </span>
              @if (!sub.isRequired) {
                <span class="chip tone-neutral" title="The parent can be finished without this">
                  Optional
                </span>
              }
            </a>
          }
        }
      </div>

      @if (canCreate && !isTerminal()) {
        <div class="card card-pad">
          <h2 class="card-title">Add a smaller task</h2>
          <mat-checkbox [(ngModel)]="isRequired" class="required-box">
            This has to be done before the main task can be finished
          </mat-checkbox>

          <mat-form-field class="full">
            <mat-label>Title</mat-label>
            <input matInput [(ngModel)]="title" />
          </mat-form-field>

          <mat-form-field class="full">
            <mat-label>Description</mat-label>
            <textarea matInput rows="2" [(ngModel)]="description"></textarea>
          </mat-form-field>

          <div class="row row-wrap">
            <app-search-select class="grow" label="Assign to (optional)"
                               nullLabel="Leave in the queue" [options]="userOptions()"
                               [(ngModel)]="assigneeUserId" />

            <mat-form-field class="hours">
              <mat-label>Estimate (h)</mat-label>
              <input matInput type="number" min="0" [(ngModel)]="estimate" />
            </mat-form-field>

            <span class="spacer"></span>
            <button matButton="filled" [disabled]="!valid() || busy()" (click)="create()">
              <mat-icon>add</mat-icon> Add it
            </button>
          </div>
        </div>
      }
    </div>
  `,
  styles: `
    .full { width: 100%; }
    .head { padding-bottom: 10px; }
    .blocked {
      display: flex; align-items: flex-start; gap: 6px; margin: 6px 0 0;
      color: var(--tone-warn-fg);
    }
    .blocked mat-icon { font-size: 17px; width: 17px; height: 17px; flex: none; margin-top: 1px; }
    .sub {
      display: flex; align-items: center; gap: 12px; flex-wrap: wrap;
      padding: 11px 20px; border-top: 1px solid var(--border);
      color: inherit; text-decoration: none;
    }
    .sub:hover { background: var(--surface-2); }
    .num { flex: none; }
    .title { flex: 1 1 200px; min-width: 0; }
    .who { flex: none; }
    .required-box { display: block; margin: 4px 0 12px; }
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

  /**
   * Read straight off the parent rather than fetched separately: the parent already carries its
   * smaller tasks, and a second request would let the two disagree on screen.
   */
  readonly parentSubtasks = computed(() => this.task().subTasks);

  /** What is actually holding the parent up — required and not finished. */
  readonly outstanding = computed(() =>
    this.parentSubtasks().filter(
      (s: SubtaskSummaryDto) =>
        s.isRequired && !['Closed', 'Cancelled', 'Duplicate'].includes(s.status)));
  readonly users = signal<AssignableUserDto[]>([]);
  readonly userOptions = computed(() => this.users().map((user) => ({
    value: user.id,
    label: user.displayName,
    chip: user.workforceState,
    chipKind: 'workforce' as const,
  })));
  readonly busy = signal(false);

  readonly canCreate = this.auth.has(Perm.taskAssign);

  title = '';
  description = '';
  isRequired = true;
  assigneeUserId: number | null = null;
  estimate: number | null = null;

  ngOnInit(): void {
    if (this.canCreate) {
      this.api.assignableUsers().subscribe((u) => this.users.set(u));
    }
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
      isRequired: this.isRequired,
    }).subscribe({
      next: (created) => {
        this.busy.set(false);
        this.title = '';
        this.description = '';
        this.assigneeUserId = null;
        this.estimate = null;
        this.isRequired = true;
        this.toast.success(`Created ${created.taskNumber}.`);
        // The parent carries the list, so refreshing it refreshes this panel.
        this.changed.emit();
      },
      error: () => this.busy.set(false),
    });
  }
}
