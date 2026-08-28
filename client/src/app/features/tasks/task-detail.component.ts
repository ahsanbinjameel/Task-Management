import {
  DestroyRef,
  Component,
  OnDestroy,
  OnInit,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTabsModule } from '@angular/material/tabs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { ApiService } from '../../core/api.service';
import { BreadcrumbsComponent, Crumb } from '../../shared/breadcrumbs.component';
import { BackLinkComponent } from '../../shared/back-link.component';
import { AuthService } from '../../core/auth.service';
import { RealtimeService, TaskChangedEvent } from '../../core/realtime.service';
import { syncOn } from '../../core/realtime-sync';
import { ToastService } from '../../core/toast.service';
import { Perm } from '../../core/permissions';
import { PARKED } from '../../core/parked';
import { DurationPipe } from '../../core/format';
import { requestTypeLabel, taskStatusLabel } from '../../core/labels';
import { AttachmentDto, RequestType, TaskDetailDto, WorkTaskStatus } from '../../core/models';
import {
  ChipComponent,
  FieldComponent,
  LoadingComponent,
  PageHeaderComponent,
} from '../../shared/ui';
import { HttpContext } from '@angular/common/http';
import { ConfirmDialog, ConfirmData, ReasonDialog, ReasonData } from '../../shared/dialogs';

/** Smaller tasks that no longer need doing — what "2 of 3 done" counts. */
const DONE_STATUSES: WorkTaskStatus[] = [
  'QCPassed', 'ReadyForClosure', 'Closed', 'Cancelled', 'Duplicate',
];
import { AssignDialogComponent, AssignDialogResult } from './assign-dialog.component';
import { StopWorkDialog, StopWorkResult } from './stop-work-dialog.component';
import { TaskCommentsComponent } from './panels/task-comments.component';
import { TaskDependenciesComponent } from './panels/task-dependencies.component';
import { TaskSubtasksComponent } from './panels/task-subtasks.component';
import { TaskScopeComponent } from './panels/task-scope.component';
import { TaskHistoryComponent } from './panels/task-history.component';
import { TaskQcComponent } from './panels/task-qc.component';
import { TaskClosureComponent } from './panels/task-closure.component';
import { AttachmentsComponent } from '../../shared/attachments.component';
import { AttachmentUploadComponent } from '../../shared/attachment-upload.component';

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
    BreadcrumbsComponent, BackLinkComponent,
    RouterLink,
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatTabsModule,
    MatTooltipModule,
    PageHeaderComponent,
    ChipComponent,
    FieldComponent,
    LoadingComponent,
    DurationPipe,
    TaskCommentsComponent,
    TaskDependenciesComponent,
    TaskSubtasksComponent,
    TaskScopeComponent,
    TaskHistoryComponent,
    TaskQcComponent,
    TaskClosureComponent,
    AttachmentsComponent,
    AttachmentUploadComponent,
  ],
  template: `
    @if (loading()) {
      <app-loading message="Loading task…" />
    } @else if (task(); as t) {
      <div class="page">
        <app-back-link fallback="/tasks" label="Tasks" />
        <app-breadcrumbs [crumbs]="crumbs(t)" />
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
              <a class="chip tone-neutral" [routerLink]="['/tasks', t.parentTaskId]">Part of a bigger task</a>
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
                {{ t.primaryAssigneeUserId ? 'Change person' : 'Assign' }}
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
            <mat-tab-group
              [selectedIndex]="tabIndex()"
              (selectedIndexChange)="selectTab($event)"
              animationDuration="0ms"
            >
              <mat-tab label="Overview">
                <div class="tab-body stack">
                  <!--
                    One brief, not four cards. Description, what was asked for and the acceptance
                    criteria are usually a line each, and giving each its own card, title and border
                    cost more height in framing than in content — a task with three short facts ran
                    past the bottom of the screen. Separated by a rule instead.
                  -->
                  <div class="card card-pad brief">
                    <section>
                      <h2 class="card-title">Description</h2>
                      <p class="body-text">{{ t.description }}</p>
                    </section>

                  <!--
                    What was asked for, in the requester's words, with their screenshots. Here so
                    that doing the work never requires opening the request: the two records stay
                    separate, the reading does not.
                  -->
                  @if (t.request; as req) {
                    <section>
                      <h2 class="card-title">What was asked for</h2>
                      <p class="muted small asked">
                        {{ req.requestedByDisplayName }} · {{ req.requestedAt | date: 'mediumDate' }}
                        @if (req.projectName) { · {{ req.projectName }} }
                        @if (req.moduleName) { · {{ req.moduleName }} }
                        @if (req.batchNumber) {
                          ·
                          <a [routerLink]="['/requests/batches', req.batchId]">{{ req.batchNumber }}</a>
                        }
                      </p>

                      <!--
                        Several requests folded into one task. Shown in full rather than as a count,
                        because a worker who cannot see the other two will finish the first and
                        call it done.
                      -->
                      @if (req.foldedWith?.length) {
                        <div class="folded">
                          <h3 class="sub">
                            Also asked for ({{ (req.foldedWith ?? []).length }} more, approved together)
                          </h3>
                          @for (other of req.foldedWith ?? []; track other.requestId) {
                            <div class="folded-item">
                              <a class="mono small muted" [routerLink]="['/requests', other.requestId]">
                                {{ other.requestNumber }}
                              </a>
                              <strong>{{ other.title }}</strong>
                              <p class="body-text">{{ other.description }}</p>
                            </div>
                          }
                        </div>
                      }

                      @if (req.originalDescription !== t.description) {
                        <p class="body-text">{{ req.originalDescription }}</p>
                      }

                      @if (req.expectedResult) {
                        <h3 class="sub">What should happen</h3>
                        <p class="body-text">{{ req.expectedResult }}</p>
                      }
                      @if (req.currentResult) {
                        <h3 class="sub">What happens instead</h3>
                        <p class="body-text">{{ req.currentResult }}</p>
                      }
                      @if (req.reproductionSteps) {
                        <h3 class="sub">Steps to reproduce</h3>
                        <p class="body-text">{{ req.reproductionSteps }}</p>
                      }
                      @if (req.businessImpact) {
                        <h3 class="sub">Why it matters</h3>
                        <p class="body-text">{{ req.businessImpact }}</p>
                      }

                      @if (req.attachments.length) {
                        <h3 class="sub">Files from the request</h3>
                        <app-attachments [attachments]="req.attachments" />
                      }
                    </section>
                  }

                    @if (t.acceptanceCriteria) {
                      <section>
                        <h2 class="card-title">Acceptance criteria</h2>
                        <p class="body-text">{{ t.acceptanceCriteria }}</p>
                      </section>
                    }

                    @if (t.resolution) {
                      <section>
                        <h2 class="card-title">Resolution</h2>
                        <p class="body-text">{{ t.resolution }}</p>
                      </section>
                    }
                  </div>

                  <!--
                    Dependencies sit in the overview when there are any: "what is holding this up"
                    belongs with the work, not behind a tab someone has to think to open. Its own
                    card, because it is a warning rather than part of the brief.
                  -->
                  @if (t.blockedBy.length) {
                    <div class="card card-pad blocked">
                      <h2 class="card-title">Waiting on other work</h2>
                      <p class="body-text">
                        This cannot start until these are finished:
                        <strong>{{ t.blockedBy.join(', ') }}</strong>
                      </p>
                    </div>
                  }

                  <!--
                    Proof sits in the Overview rather than behind the quality-check tab: the
                    checker looks at it before anything else, and the person who has to supply it
                    is not the person the QC tab is for.
                  -->
                  @if (showProof()) {
                    <div class="card card-pad">
                      <h2 class="card-title">Proof of work</h2>
                      <app-attachments
                        [attachments]="t.completionProof ?? []"
                        emptyText="Nothing attached yet." />
                      @if (canAddProof()) {
                        <app-attachment-upload
                          [taskId]="t.id" kind="CompletionProof"
                          label="Attach proof" icon="verified"
                          (uploaded)="proofAdded($event)" />
                      }
                    </div>
                  }

                  @if (t.attachments?.length) {
                    <div class="card card-pad">
                      <h2 class="card-title">Files on this task</h2>
                      <app-attachments [attachments]="t.attachments ?? []" />
                    </div>
                  }

                  <!--
                    Only once there is something to show. A card whose whole content is "no time
                    logged yet" is a heading, a border and a sentence saying nothing has happened —
                    the total is already in Details, where a reader looking for it goes.
                  -->
                  @if (!auth.isRequesterOnly() && t.workSessions.length > 0) {
                  <div class="card card-pad">
                    <h2 class="card-title">Work sessions</h2>
                    @for (s of t.workSessions; track s.id) {
                        <div class="session">
                          <span class="mono small">{{
                            s.sessionStart | date: 'MMM d, HH:mm'
                          }}</span>
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
                  </div>
                  }
                </div>
              </mat-tab>

              <mat-tab label="Updates">
                <div class="tab-body">
                  <app-task-comments [taskId]="t.id" />
                </div>
              </mat-tab>

              <!--
                Quality check earns a tab when it is actually part of this task's life — while it
                is being checked, once it has been checked, or for the people who do the checking.
                A worker starting a fresh task does not need a permanently empty QC tab.
              -->
              @if (showQualityCheck(t)) {
                <mat-tab [label]="'Quality check'
                                  + (t.qcReviews.length ? ' (' + t.qcReviews.length + ')' : '')">
                  <div class="tab-body">
                    <app-task-qc [task]="t" (changed)="reload()" />
                  </div>
                </mat-tab>
              }

              <!--
                Dependencies, subtasks and scope changes are parked (PRODUCT-CORE §10) — see
                core/parked.ts. All three still work: the endpoints, permissions and panels are
                untouched, and a ?tab= link falls back to the overview rather than breaking.
                They are three tabs of ceremony on the screen a worker opens to do the work, and
                none has yet been asked for by anyone using the system.

                Editing the dependency graph is a coordinating job; reading it stays in Overview.
              -->
              @if (!PARKED.dependencies && canCoordinate()) {
                <mat-tab label="Dependencies">
                  <div class="tab-body">
                    <app-task-dependencies [taskId]="t.id" (changed)="reload()" />
                  </div>
                </mat-tab>
              }

              @if (!PARKED.subtasks) {
                <mat-tab [label]="subtaskLabel(t)">
                  <div class="tab-body">
                    <app-task-subtasks [task]="t" (changed)="reload()" />
                  </div>
                </mat-tab>
              }

              @if (!PARKED.scopeChanges && canCoordinate()) {
                <mat-tab label="Scope">
                  <div class="tab-body">
                    <app-task-scope [taskId]="t.id" (changed)="reload()" />
                  </div>
                </mat-tab>
              }

              <!--
                The history tab is not rendered for a requester, because the server does not send
                them one. Showing an empty timeline would read as "nothing has happened", which is
                a worse answer than not asking the question.
              -->
              @if (!auth.isRequesterOnly()) {
                <mat-tab label="History">
                  <div class="tab-body">
                    <app-task-history [task]="t" />
                  </div>
                </mat-tab>
              }
            </mat-tab-group>
          </div>

          <aside class="side stack">
            <div class="card card-pad">
              <h2 class="card-title">Details</h2>
              <!--
                Where in the product this is. Sits directly under the client because together they
                are the two axes a request is placed on (PRODUCT-CORE §5) — the client is which
                instance, this is which part of the product.
              -->
              @if (t.clientName) {
                <app-field label="Client">
                  {{ t.clientName }}
                </app-field>
              }
              <app-field label="Responsible">
                {{ t.primaryAssigneeDisplayName ?? 'Nobody yet' }}
              </app-field>
              @if (t.supportPeople.length > 0) {
                <app-field label="Support">
                  @for (p of t.supportPeople; track p.userId) {
                    <div>{{ p.displayName }}</div>
                  }
                </app-field>
              }
              @if (!auth.isRequesterOnly()) {
              <app-field label="Estimate">
                {{ t.estimatedEffortHours ? t.estimatedEffortHours + 'h' : '—' }}
              </app-field>
              <app-field label="Time logged">{{ t.totalWorkedTime | duration }}</app-field>
              }
              <app-field label="Due">
                @if (t.dueDate) {
                  <span [class.overdue]="overdue()">{{ t.dueDate | date: 'mediumDate' }}</span>
                } @else {
                  —
                }
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
    /* Sections divided by a rule rather than by four separate cards. */
    .brief > section + section {
      margin-top: 14px; padding-top: 14px; border-top: 1px solid var(--border);
    }
    .brief .card-title { margin-top: 0; }

    .asked { margin: -4px 0 12px; }
    .folded { margin-top: 12px; border-left: 3px solid var(--border); padding-left: 12px; }
    .folded-item { margin-bottom: 10px; }
    .folded-item strong { margin-left: 8px; }
    .folded-item .body-text { margin-top: 2px; }
    .sub {
      font-size: 12px; text-transform: uppercase; letter-spacing: 0.04em;
      color: var(--text-muted); margin: 16px 0 4px; font-weight: 600;
    }
    .blocked { border-left: 3px solid var(--tone-warn-fg); }
    .hint {
      margin-top: 2px;
      line-height: 1.35;
    }
    .summary {
      display: flex;
      align-items: center;
      gap: 14px;
      flex-wrap: wrap;
      padding: 14px 18px;
      margin-bottom: 18px;
    }
    .chips {
      display: flex;
      gap: 8px;
      align-items: center;
      flex-wrap: wrap;
    }
    .actions {
      display: flex;
      gap: 8px;
      margin-left: auto;
      flex-wrap: wrap;
    }
    .complete {
      --mdc-filled-button-container-color: #17603a;
    }
    .proof-lead { margin: -4px 0 12px; }
    .banner {
      display: flex;
      align-items: center;
      gap: 13px;
      padding: 13px 17px;
      border-radius: var(--radius);
      margin-bottom: 14px;
    }
    .banner.danger {
      background: var(--tone-danger-bg);
      color: var(--tone-danger-fg);
    }
    .layout {
      display: grid;
      gap: 18px;
      grid-template-columns: minmax(0, 1fr) 300px;
    }
    @media (max-width: 1150px) {
      .layout {
        grid-template-columns: 1fr;
      }
    }
    .tab-body {
      padding-top: 16px;
    }
    .body-text {
      margin: 0;
      white-space: pre-wrap;
      line-height: 1.55;
      font-size: 14px;
    }
    .session {
      display: flex;
      align-items: center;
      gap: 9px;
      padding: 7px 0;
      border-top: 1px solid var(--border);
    }
    .session:first-of-type {
      border-top: none;
    }
    a.chip {
      text-decoration: none;
    }
  `,
})
export class TaskDetailComponent implements OnInit, OnDestroy {
  /** Capabilities hidden while the product freeze is on — see `core/parked.ts`. */
  protected readonly PARKED = PARKED;

  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);

  /** Set by `?start=1`, from a "Start work" row action. Consumed once the task has loaded. */
  private pendingStart = false;
  /** Bound from the route by withComponentInputBinding(). */
  readonly id = input.required<string>();

  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly realtime = inject(RealtimeService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly task = signal<TaskDetailDto | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);

  /**
   * Which tab is open, as a name in the URL rather than an index in a signal.
   *
   * Three things fall out of that. A link can point at a tab — `?tab=qc` from a notification lands
   * on the quality check instead of the overview, so the reader does not have to hunt for the
   * thing they were told about. Browser Back walks the tabs, which is what everyone expects once
   * the URL changes. And the index stops being a magic number: the tab list is conditional, so
   * `tabIndex.set(2)` was only ever right by coincidence.
   */
  readonly tab = signal<string>('overview');

  /**
   * The tabs actually rendered, in render order. This has to mirror the template's `@if`s —
   * mat-tab-group only knows about indices, so the mapping has to exist somewhere. Keeping it
   * here, next to the visibility rules it depends on, is the least bad place for it.
   */
  readonly tabKeys = computed<string[]>(() => {
    const t = this.task();
    if (!t) return ['overview'];

    const keys = ['overview', 'updates'];
    if (this.showQualityCheck(t)) keys.push('qc');
    if (!PARKED.dependencies && this.canCoordinate()) keys.push('dependencies');
    if (!PARKED.subtasks) keys.push('subtasks');
    if (!PARKED.scopeChanges && this.canCoordinate()) keys.push('scope');
    if (!this.auth.isRequesterOnly()) keys.push('history');
    return keys;
  });

  /** An unknown or now-hidden tab falls back to the overview rather than showing nothing. */
  readonly tabIndex = computed(() => Math.max(0, this.tabKeys().indexOf(this.tab())));

  /** Written to the URL with `replaceUrl: false`, so Back returns to the previous tab. */
  selectTab(index: number): void {
    const key = this.tabKeys()[index] ?? 'overview';
    if (key === this.tab()) return;

    this.tab.set(key);
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab: key === 'overview' ? null : key },
      queryParamsHandling: 'merge',
    });
  }

  private taskId = 0;

  /** The chain this piece of work actually travelled, not the URL segments. */
  crumbs(t: TaskDetailDto): Crumb[] {
    const trail: Crumb[] = [{ label: 'Tasks', route: '/tasks' }];

    if (t.requestId && t.requestNumber) {
      trail.push({ label: t.requestNumber, route: ['/requests', t.requestId] });
    }
    if (t.parentTaskId) {
      trail.push({ label: 'Bigger task', route: ['/tasks', t.parentTaskId] });
    }

    trail.push({ label: t.taskNumber });
    return trail;
  }

  readonly isMine = computed(() => this.task()?.primaryAssigneeUserId === this.auth.user()?.id);
  readonly isRunning = computed(
    () => this.isMine() && this.task()?.workSessions.some((s) => s.status === 'Active') === true,
  );

  readonly canStart = computed(() => {
    const t = this.task();
    if (!t || !this.isMine() || this.isRunning()) return false;
    if (t.blockedBy.length > 0) return false;
    return ['Assigned', 'ReadyToStart', 'Paused', 'Blocked', 'QCFailedRework', 'Reopened'].includes(
      t.status,
    );
  });

  readonly canStartQc = computed(() => {
    const t = this.task();
    return (
      !!t &&
      t.status === 'CompletedReadyForQC' &&
      this.auth.has(Perm.taskQCReview) &&
      t.primaryAssigneeUserId !== this.auth.user()?.id
    );
  });

  readonly canAssign = computed(() => this.auth.has(Perm.taskAssign) && !this.isTerminal());

  /**
   * Only the person responsible for the work can attach the proof of it — the same rule the server
   * enforces, so the control is never offered to somebody who would be refused. Once the task is
   * closed the record stops changing, proof included.
   */
  readonly canAddProof = computed(() => this.isMine() && !this.isTerminal());

  /**
   * The card is shown once proof exists, and to the person who owes it from the moment work can
   * start. A requester never owes any, so an empty card on their screen would only read as a
   * missing thing they cannot supply.
   */
  readonly showProof = computed(() =>
    (this.task()?.completionProof?.length ?? 0) > 0 || this.canAddProof());

  /**
   * Which tabs a reader gets.
   *
   * A worker's screen is Overview, Updates, Smaller tasks and History — the four things doing the
   * work involves. Dependencies and Scope are coordination: reading "what is holding this up" is
   * in the Overview for everyone, editing the graph is not.
   */
  readonly canCoordinate = computed(() =>
    this.auth.has(Perm.taskAssign) || this.auth.has(Perm.taskApprove));

  /**
   * Quality check earns its tab when it is part of this task's life: while it is being checked,
   * once it has been, or for the people whose job is checking. Otherwise it is a permanently
   * empty tab that everyone learns to skip.
   */
  showQualityCheck(task: TaskDetailDto): boolean {
    const inCheck: WorkTaskStatus[] =
      ['CompletedReadyForQC', 'QCReview', 'QCPassed', 'QCFailedRework', 'ReadyForClosure', 'Closed'];

    // A requester is never sent the attempts, so the tab could only ever be empty for them.
    if (this.auth.isRequesterOnly()) return false;

    return task.qcReviews.length > 0
      || inCheck.includes(task.status)
      || this.auth.has(Perm.taskQCReview);
  }

  /** "Smaller tasks — 2 of 3 done" reads as progress; a bare count does not. */
  subtaskLabel(task: TaskDetailDto): string {
    if (task.subTasks.length === 0) return 'Smaller tasks';

    const done = task.subTasks.filter((s) => DONE_STATUSES.includes(s.status)).length;
    return `Smaller tasks — ${done} of ${task.subTasks.length} done`;
  }

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
      'InProgress',
      'Paused',
      'Blocked',
      'CompletedReadyForQC',
      'QCReview',
      'QCPassed',
      'QCFailedRework',
      'Closed',
      'Reopened',
      'Assigned',
    ];

    return t.availableTransitions.filter((s) => !handledElsewhere.includes(s));
  });

  ngOnInit(): void {
    this.taskId = Number(this.id());
    this.pendingStart = this.route.snapshot.queryParamMap.get('start') === '1';
    this.load();

    // Subscribed rather than read once, so Back through the tab history actually moves the tab.
    this.route.queryParamMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => this.tab.set(params.get('tab') ?? 'overview'));

    this.realtime.subscribeToTask(this.taskId);

    // Re-fetch rather than patch: the event is a pointer, and the record is the truth. Filtered to
    // this task so an unrelated one does not cause a reload.
    syncOn<TaskChangedEvent>([this.realtime.taskChanged], () => this.load(false), this.destroyRef, {
      filter: (e) => e.taskId === this.taskId,
    });
  }

  ngOnDestroy(): void {
    this.realtime.unsubscribeFromTask(this.taskId);
  }

  reload(): void {
    this.load(false);
  }

  private load(showSpinner = true): void {
    if (showSpinner) this.loading.set(true);

    this.api.task(this.taskId).subscribe({
      next: (t) => {
        this.task.set(t);
        this.loading.set(false);
        this.startIfAsked();
      },
      error: () => this.loading.set(false),
    });
  }

  /**
   * "Start work" on a queue row means start it, not "open it and find the button".
   *
   * Done here rather than in the list because starting is guarded — one active session per person,
   * nothing blocked by unfinished work — and this screen is where those refusals are already
   * shown. The flag is consumed on arrival so a refresh does not silently start the timer again.
   */
  private startIfAsked(): void {
    if (!this.pendingStart) return;
    this.pendingStart = false;

    if (this.canStart()) this.start();
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

  label = (value: string) => requestTypeLabel(value as RequestType);
  isTerminal = () => ['Closed', 'Cancelled', 'Duplicate'].includes(this.task()?.status ?? '');

  // --- the timer ------------------------------------------------------------------------------

  start(): void {
    // Starting is not just a button: it opens a work session that counts towards this task, and
    // the one-active-session rule means whatever else was running is closed in the same commit.
    // Reachable in one click from a queue row (?start=1), which is exactly the path where someone
    // meant to open the task and read it — so the timer must not start unannounced.
    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: {
          title: 'Start work on this task?',
          message:
            'The timer starts now and the time counts against this task. Anything else you had '
            + 'running is paused — only one task can be active at a time.',
          confirmText: 'Start the timer',
          submit: (ctx: HttpContext) => this.api.startWork(this.taskId, ctx),
        },
      })
      .afterClosed()
      .subscribe((task?: unknown) => {
        if (!task) return;
        this.task.set(task as TaskDetailDto);
        this.toast.success('Timer started.');
      });
  }

  pause(): void {
    this.stopWork('pause');
  }

  block(): void {
    this.stopWork('block');
  }

  private stopWork(mode: 'pause' | 'block'): void {
    this.dialog
      .open(StopWorkDialog, { data: { mode, taskId: this.taskId } })
      .afterClosed()
      .subscribe((updated?: StopWorkResult) => {
        // Already saved by the dialog, which stayed open if it had failed.
        if (!updated) return;
        this.task.set(updated);
        this.toast.success(mode === 'pause' ? 'Paused.' : 'Marked as cannot continue.');
      });
  }

  /**
   * Folded into the task the client already holds rather than re-fetched. The upload is the whole
   * change — nothing else on the record moved — and a round trip here would blank the screen to
   * redraw one thumbnail.
   */
  proofAdded(attachment: AttachmentDto): void {
    this.task.update((t) => (t
      ? { ...t, completionProof: [...(t.completionProof ?? []), attachment] }
      : t));
  }

  complete(): void {
    const hasProof = (this.task()?.completionProof?.length ?? 0) > 0;

    this.dialog
      .open<ReasonDialog, ReasonData>(ReasonDialog, {
        data: {
          title: 'Finish this work',
          message:
            'This sends the task for a quality check. It does not close it — only the '
            + 'quality check can do that.'
            // Said here rather than enforced, because a task whose result is not a screenshot is
            // ordinary. Refusing to accept the work without a file would only teach people to
            // attach anything.
            + (hasProof ? '' : ' Nothing is attached as proof yet — a screenshot of the result '
              + 'saves the person checking it a round of questions.'),
          label: 'What did you do? (a short summary for whoever checks it)',
          required: false,
          confirmText: 'Send for checking',
          submit: (resolution: string, ctx) =>
            this.api.completeWork(this.taskId, resolution || undefined, ctx),
        },
      })
      .afterClosed()
      .subscribe((updated?: unknown) => {
        if (!updated) return;
        this.task.set(updated as TaskDetailDto);
        this.toast.success('Sent for quality check.');
      });
  }

  // --- workflow -------------------------------------------------------------------------------

  startQc(): void {
    this.busy.set(true);
    this.api.startQC(this.taskId).subscribe({
      next: (t) => {
        this.task.set(t);
        this.busy.set(false);
        this.selectTab(this.tabKeys().indexOf('qc'));
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
      .subscribe((assigned?: AssignDialogResult) => {
        // Already saved by the dialog; just reflect it.
        if (!assigned) return;
        this.task.set(assigned);
        this.toast.success('Responsible person updated.');
      });
  }

  transition(to: WorkTaskStatus): void {
    // Cancel, defer and hold all demand a reason server-side; asking here beats a 400.
    const needsReason: WorkTaskStatus[] = [
      'Cancelled',
      'Deferred',
      'OnHold',
      'Duplicate',
      'ClarificationRequired',
      'ReadyForAssignment',
    ];

    if (!needsReason.includes(to)) {
      this.run(this.api.transition(this.taskId, { to }), `Moved to ${taskStatusLabel(to)}.`);
      return;
    }

    this.dialog
      .open<ReasonDialog, ReasonData>(ReasonDialog, {
        data: {
          title: `Move to ${taskStatusLabel(to)}`,
          label: 'Why?',
          danger: to === 'Cancelled',
          confirmText: taskStatusLabel(to),
          submit: (reason: string, ctx) => this.api.transition(this.taskId, { to, reason }, ctx),
        },
      })
      .afterClosed()
      .subscribe((updated?: unknown) => {
        if (!updated) return;
        this.task.set(updated as TaskDetailDto);
        this.toast.success(`Moved to ${taskStatusLabel(to)}.`);
      });
  }
}
