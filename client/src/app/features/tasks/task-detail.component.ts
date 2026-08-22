import { Component, OnDestroy, OnInit, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { RealtimeService } from '../../core/realtime.service';
import { ToastService } from '../../core/toast.service';
import { Perm } from '../../core/permissions';
import { DurationPipe, HumanizePipe, humanizeEnum } from '../../core/format';
import { TaskDetailDto, WorkTaskStatus } from '../../core/models';
import {
  ChipComponent, FieldComponent, LoadingComponent, PageHeaderComponent,
} from '../../shared/ui';
import { ConfirmDialog, ReasonDialog } from '../../shared/dialogs';
import { AssignDialogComponent, AssignDialogResult } from './assign-dialog.component';
import { StopWorkDialog, StopWorkResult } from './stop-work-dialog.component';
import { TaskCommentsComponent } from './panels/task-comments.component';
import { TaskDependenciesComponent } from './panels/task-dependencies.component';
import { TaskSubtasksComponent } from './panels/task-subtasks.component';
import { TaskScopeComponent } from './panels/task-scope.component';
import { TaskHistoryComponent } from './panels/task-history.component';
import { TaskQcComponent } from './panels/task-qc.component';
import { TaskClosureComponent } from './panels/task-closure.component';

/**
 * The task screen.
 *
 * Two things drive the whole layout. The **action bar** shows only what this person can actually do
 * to this task right now — derived from the server's `availableTransitions` plus the timer rules —
 * so nobody hunts for a button that would 409. And the screen **re-fetches on every real-time
 * event** for this task rather than patching state from the payload, because the server sends
 * pointers, not records.
 */
@Component({
  selector: 'app-task-detail',
  standalone: true,
  imports: [
    RouterLink, DatePipe, MatButtonModule, MatIconModule, MatMenuModule, MatTabsModule,
    MatTooltipModule, PageHeaderComponent, ChipComponent, FieldComponent, LoadingComponent,
    DurationPipe, HumanizePipe,
    TaskCommentsComponent, TaskDependenciesComponent, TaskSubtasksComponent, TaskScopeComponent,
    TaskHistoryComponent, TaskQcComponent, TaskClosureComponent,
  ],
  template: `
    @if (loading()) {
      <app-loading message="Loading task…" />
    } @else if (task(); as t) {
      <div class="page">
        <app-page-header [title]="t.title" [subtitle]="t.taskNumber + ' · ' + label(t.type)">
          @if (t.requestId) {
            <a matButton [routerLink]="['/requests', t.requestId]">
              <mat-icon>inbox</mat-icon> {{ t.requestNumber }}
            </a>
          }
          <button matButton (click)="reload()"><mat-icon>refresh</mat-icon></button>
        </app-page-header>

        <!-- Everything blocking or running, stated before the detail. -->
        @if (t.blockedBy.length > 0) {
          <div class="banner danger">
            <mat-icon>block</mat-icon>
            <div>
              <strong>Waiting on {{ t.blockedBy.join(', ') }}</strong>
              <div class="small">
                Work cannot start until those are finished. See the Dependencies tab.
              </div>
            </div>
          </div>
        }

        <div class="summary card">
          <div class="chips">
            <app-chip [value]="t.status" kind="status" [dot]="true" />
            <app-chip [value]="t.priority" kind="priority" />
            @if (t.parentTaskId) {
              <a class="chip tone-neutral" [routerLink]="['/tasks', t.parentTaskId]">Subtask</a>
            }
          </div>

          <div class="actions">
            @if (canStart()) {
              <button matButton="filled" (click)="start()" [disabled]="busy()">
                <mat-icon>play_arrow</mat-icon> Start work
              </button>
            }
            @if (isRunning()) {
              <button matButton="filled" (click)="pause()" [disabled]="busy()">
                <mat-icon>pause</mat-icon> Pause
              </button>
              <button matButton (click)="block()" [disabled]="busy()">
                <mat-icon>block</mat-icon> Blocked
              </button>
              <button matButton="filled" class="complete" (click)="complete()" [disabled]="busy()">
                <mat-icon>check</mat-icon> Complete
              </button>
            }
            @if (canStartQc()) {
              <button matButton="filled" (click)="startQc()" [disabled]="busy()">
                <mat-icon>verified</mat-icon> Start QC review
              </button>
            }
            @if (canAssign()) {
              <button matButton (click)="assign()" [disabled]="busy()">
                <mat-icon>person_add</mat-icon>
                {{ t.primaryAssigneeUserId ? 'Reassign' : 'Assign' }}
              </button>
            }

            @if (otherTransitions().length > 0) {
              <button matButton [matMenuTriggerFor]="more" [disabled]="busy()">
                More <mat-icon iconPositionEnd>expand_more</mat-icon>
              </button>
              <mat-menu #more="matMenu">
                @for (target of otherTransitions(); track target) {
                  <button mat-menu-item (click)="transition(target)">
                    Move to {{ label(target) }}
                  </button>
                }
              </mat-menu>
            }
          </div>
        </div>

        <div class="layout">
          <div class="main">
            <mat-tab-group [selectedIndex]="tabIndex()" (selectedIndexChange)="tabIndex.set($event)"
                           animationDuration="0ms">
              <mat-tab label="Overview">
                <div class="tab-body stack">
                  <div class="card card-pad">
                    <h2 class="card-title">Description</h2>
                    <p class="body-text">{{ t.description }}</p>
                  </div>

                  @if (t.acceptanceCriteria) {
                    <div class="card card-pad">
                      <h2 class="card-title">Acceptance criteria</h2>
                      <p class="body-text">{{ t.acceptanceCriteria }}</p>
                    </div>
                  }

                  @if (t.resolution) {
                    <div class="card card-pad">
                      <h2 class="card-title">Resolution</h2>
                      <p class="body-text">{{ t.resolution }}</p>
                    </div>
                  }

                  <div class="card card-pad">
                    <h2 class="card-title">Work sessions</h2>
                    @if (t.workSessions.length === 0) {
                      <p class="muted small">No time logged yet.</p>
                    } @else {
                      @for (s of t.workSessions; track s.id) {
                        <div class="session">
                          <span class="mono small">{{ s.sessionStart | date: 'MMM d, HH:mm' }}</span>
                          <span class="muted small">→</span>
                          <span class="mono small">
                            {{ s.sessionEnd ? (s.sessionEnd | date: 'HH:mm') : 'running' }}
                          </span>
                          <span class="spacer"></span>
                          @if (s.endPauseReasonName) {
                            <span class="chip tone-muted">{{ s.endPauseReasonName }}</span>
                          }
                          @if (s.endedByInterruption) {
                            <span class="chip tone-warn">Interrupted</span>
                          }
                          <span class="mono small">{{ s.duration | duration }}</span>
                        </div>
                      }
                    }
                  </div>
                </div>
              </mat-tab>

              <mat-tab label="Comments">
                <div class="tab-body">
                  <app-task-comments [taskId]="t.id" />
                </div>
              </mat-tab>

              <mat-tab [label]="'QC' + (t.qcReviews.length ? ' (' + t.qcReviews.length + ')' : '')">
                <div class="tab-body">
                  <app-task-qc [task]="t" (changed)="reload()" />
                </div>
              </mat-tab>

              <mat-tab label="Dependencies">
                <div class="tab-body">
                  <app-task-dependencies [taskId]="t.id" (changed)="reload()" />
                </div>
              </mat-tab>

              <mat-tab [label]="'Subtasks' + (t.subTaskIds.length ? ' (' + t.subTaskIds.length + ')' : '')">
                <div class="tab-body">
                  <app-task-subtasks [task]="t" (changed)="reload()" />
                </div>
              </mat-tab>

              <mat-tab label="Scope">
                <div class="tab-body">
                  <app-task-scope [taskId]="t.id" (changed)="reload()" />
                </div>
              </mat-tab>

              <mat-tab label="History">
                <div class="tab-body">
                  <app-task-history [task]="t" />
                </div>
              </mat-tab>
            </mat-tab-group>
          </div>

          <aside class="side stack">
            <div class="card card-pad">
              <h2 class="card-title">Details</h2>
              <app-field label="Assignee">
                {{ t.primaryAssigneeDisplayName ?? 'Unassigned' }}
              </app-field>
              <app-field label="Estimate">
                {{ t.estimatedEffortHours ? t.estimatedEffortHours + 'h' : '—' }}
              </app-field>
              <app-field label="Time logged">{{ t.totalWorkedTime | duration }}</app-field>
              <app-field label="Due">
                @if (t.dueDate) {
                  <span [class.overdue]="overdue()">{{ t.dueDate | date: 'mediumDate' }}</span>
                } @else { — }
              </app-field>
              <app-field label="Progress">{{ t.progressPercent }}%</app-field>
            </div>

            <app-task-closure [task]="t" (changed)="reload()" />
          </aside>
        </div>
      </div>
    } @else {
      <div class="page">
        <p class="muted">That task could not be loaded.</p>
      </div>
    }
  `,
  styles: `
    .summary {
      display: flex; align-items: center; gap: 14px; flex-wrap: wrap;
      padding: 14px 18px; margin-bottom: 18px;
    }
    .chips { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    .actions { display: flex; gap: 8px; margin-left: auto; flex-wrap: wrap; }
    .complete { --mdc-filled-button-container-color: #17603a; }
    .banner {
      display: flex; align-items: center; gap: 13px; padding: 13px 17px;
      border-radius: var(--radius); margin-bottom: 14px;
    }
    .banner.danger { background: var(--tone-danger-bg); color: var(--tone-danger-fg); }
    .layout { display: grid; gap: 18px; grid-template-columns: minmax(0, 1fr) 300px; }
    @media (max-width: 1150px) { .layout { grid-template-columns: 1fr; } }
    .tab-body { padding-top: 16px; }
    .body-text { margin: 0; white-space: pre-wrap; line-height: 1.55; font-size: 14px; }
    .session {
      display: flex; align-items: center; gap: 9px;
      padding: 7px 0; border-top: 1px solid var(--border);
    }
    .session:first-of-type { border-top: none; }
    a.chip { text-decoration: none; }
  `,
})
export class TaskDetailComponent implements OnInit, OnDestroy {
  /** Bound from the route by withComponentInputBinding(). */
  readonly id = input.required<string>();

  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly realtime = inject(RealtimeService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly task = signal<TaskDetailDto | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly tabIndex = signal(0);

  private taskId = 0;

  readonly isMine = computed(() => this.task()?.primaryAssigneeUserId === this.auth.user()?.id);
  readonly isRunning = computed(() =>
    this.isMine() && this.task()?.workSessions.some((s) => s.status === 'Active') === true);

  readonly canStart = computed(() => {
    const t = this.task();
    if (!t || !this.isMine() || this.isRunning()) return false;
    if (t.blockedBy.length > 0) return false;
    return ['Assigned', 'ReadyToStart', 'Paused', 'Blocked', 'QCFailedRework', 'Reopened']
      .includes(t.status);
  });

  readonly canStartQc = computed(() => {
    const t = this.task();
    return !!t && t.status === 'CompletedReadyForQC' && this.auth.has(Perm.taskQCReview)
      && t.primaryAssigneeUserId !== this.auth.user()?.id;
  });

  readonly canAssign = computed(() =>
    this.auth.has(Perm.taskAssign) && !this.isTerminal());

  readonly overdue = computed(() => {
    const t = this.task();
    return !!t?.dueDate && new Date(t.dueDate) < new Date() && t.status !== 'Closed';
  });

  /**
   * Transitions offered in the overflow menu. The dedicated buttons and endpoints own the rest:
   * QC verdicts and closure are refused by the generic endpoint precisely so they leave their
   * records behind, and offering them here would only produce a 409.
   */
  readonly otherTransitions = computed(() => {
    const t = this.task();
    if (!t) return [];

    const handledElsewhere: WorkTaskStatus[] = [
      'InProgress', 'Paused', 'Blocked', 'CompletedReadyForQC', 'QCReview',
      'QCPassed', 'QCFailedRework', 'Closed', 'Reopened', 'Assigned',
    ];

    return t.availableTransitions.filter((s) => !handledElsewhere.includes(s));
  });

  ngOnInit(): void {
    this.taskId = Number(this.id());
    this.load();

    this.realtime.subscribeToTask(this.taskId);
    this.realtime.taskChanged.subscribe((e) => {
      // Re-fetch rather than patch: the event is a pointer, and the record is the truth.
      if (e.taskId === this.taskId) this.load(false);
    });
  }

  ngOnDestroy(): void {
    this.realtime.unsubscribeFromTask(this.taskId);
  }

  reload(): void { this.load(false); }

  private load(showSpinner = true): void {
    if (showSpinner) this.loading.set(true);

    this.api.task(this.taskId).subscribe({
      next: (t) => { this.task.set(t); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  private run(call: import('rxjs').Observable<TaskDetailDto>, message: string): void {
    this.busy.set(true);
    call.subscribe({
      next: (t) => {
        this.task.set(t);
        this.busy.set(false);
        this.toast.success(message);
      },
      error: () => this.busy.set(false),
    });
  }

  label = (value: string) => humanizeEnum(value);
  isTerminal = () => ['Closed', 'Cancelled', 'Duplicate'].includes(this.task()?.status ?? '');

  // --- the timer ------------------------------------------------------------------------------

  start(): void {
    this.run(this.api.startWork(this.taskId), 'Timer started.');
  }

  pause(): void {
    this.stopWork('pause');
  }

  block(): void {
    this.stopWork('block');
  }

  private stopWork(mode: 'pause' | 'block'): void {
    this.dialog
      .open(StopWorkDialog, { data: { mode } })
      .afterClosed()
      .subscribe((result?: StopWorkResult) => {
        if (!result) return;

        const call = mode === 'pause'
          ? this.api.pauseWork(this.taskId, result.pauseReasonId, result.comment)
          : this.api.blockWork(this.taskId, result.pauseReasonId, result.comment);

        this.run(call, mode === 'pause' ? 'Paused.' : 'Marked as blocked.');
      });
  }

  complete(): void {
    this.dialog
      .open(ReasonDialog, {
        data: {
          title: 'Complete this task',
          message: 'This sends the task to QC. It does not close it — only QC can do that.',
          label: 'What did you do? (resolution)',
          confirmText: 'Complete',
        },
      })
      .afterClosed()
      .subscribe((resolution?: string) => {
        if (resolution === undefined) return;
        this.run(this.api.completeWork(this.taskId, resolution), 'Sent to QC.');
      });
  }

  // --- workflow -------------------------------------------------------------------------------

  startQc(): void {
    this.busy.set(true);
    this.api.startQC(this.taskId).subscribe({
      next: (t) => {
        this.task.set(t);
        this.busy.set(false);
        this.tabIndex.set(2);
        this.toast.success('You are now the QC reviewer for this task.');
      },
      error: () => this.busy.set(false),
    });
  }

  assign(): void {
    const t = this.task();
    if (!t) return;

    this.dialog
      .open(AssignDialogComponent, {
        data: {
          task: t,
          isReassign: !!t.primaryAssigneeUserId,
          currentAssigneeId: t.primaryAssigneeUserId,
          rowVersion: t.rowVersion,
        },
      })
      .afterClosed()
      .subscribe((result?: AssignDialogResult) => {
        if (!result) return;
        this.run(
          this.api.assign(this.taskId, result.assigneeUserId, result.reason, result.rowVersion),
          'Assignment updated.');
      });
  }

  transition(to: WorkTaskStatus): void {
    // Cancel, defer and hold all demand a reason server-side; asking here beats a 400.
    const needsReason: WorkTaskStatus[] = ['Cancelled', 'Deferred', 'OnHold', 'Duplicate',
      'ClarificationRequired', 'ReadyForAssignment'];

    if (!needsReason.includes(to)) {
      this.run(this.api.transition(this.taskId, { to }), `Moved to ${humanizeEnum(to)}.`);
      return;
    }

    this.dialog
      .open(ReasonDialog, {
        data: {
          title: `Move to ${humanizeEnum(to)}`,
          label: 'Reason',
          danger: to === 'Cancelled',
          confirmText: humanizeEnum(to),
        },
      })
      .afterClosed()
      .subscribe((reason?: string) => {
        if (!reason) return;
        this.run(this.api.transition(this.taskId, { to, reason }), `Moved to ${humanizeEnum(to)}.`);
      });
  }
}
