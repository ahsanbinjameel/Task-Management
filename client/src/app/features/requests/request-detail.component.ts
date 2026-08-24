import { DestroyRef, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RealtimeService, RequestChangedEvent } from '../../core/realtime.service';
import { syncOn } from '../../core/realtime-sync';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../../core/api.service';
import { BreadcrumbsComponent, Crumb } from '../../shared/breadcrumbs.component';
import { RequestEditDialog } from './request-edit-dialog.component';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { Perm } from '../../core/permissions';
import { saveBlob } from '../../core/format';
import { requestTypeLabel } from '../../core/labels';
import { Priority, RequestDetailDto, RequestType, TriageOutcome } from '../../core/models';
import { enumOptions, SearchSelectComponent } from '../../shared/search-select.component';
import { AttachmentsComponent } from '../../shared/attachments.component';
import {
  ChipComponent, FieldComponent, LoadingComponent, PageHeaderComponent,
} from '../../shared/ui';

/**
 * A request, and — for a reviewer — the triage panel.
 *
 * Triage is the gate between "someone asked" and "someone is doing it". Five of its six outcomes
 * end the request without creating any work, and only Approve produces a task. The panel makes that
 * asymmetry visible instead of burying it in a dropdown.
 */
@Component({
  selector: 'app-request-detail',
  standalone: true,
  imports: [
    BreadcrumbsComponent,
    DatePipe, FormsModule, RouterLink, MatButtonModule, MatButtonToggleModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatTooltipModule,
    PageHeaderComponent, ChipComponent, FieldComponent, LoadingComponent,
    SearchSelectComponent, AttachmentsComponent,
  ],
  template: `
    @if (loading()) {
      <app-loading message="Loading request…" />
    } @else if (request(); as r) {
      <div class="page">
        <app-breadcrumbs [crumbs]="crumbs(r)" />
        <app-page-header [title]="r.title" [subtitle]="r.requestNumber + ' · ' + label(r.type)">
          @if (canEdit()) {
            <button matButton (click)="edit(r)"><mat-icon>edit</mat-icon> Change this request</button>
          }
          <!--
            The requester is never sent to the task. Their request is the record of it, and the
            progress panel below says everything the task screen would have told them. People who
            coordinate the work keep the link, because for them the task is the thing they act on.
          -->
          @if (r.generatedTaskId && canSeeTask()) {
            <a matButton [routerLink]="['/tasks', r.generatedTaskId]">
              <mat-icon>task_alt</mat-icon> View task
            </a>
          }
        </app-page-header>

        <div class="layout">
          <div class="stack">
            <!--
              What is happening, first and in plain words. This is the answer to the only question
              most requesters open the screen with, so it goes above the thing they already wrote.
            -->
            @if (r.progress; as p) {
              <div class="card card-pad progress-card">
                <div class="row row-wrap chips">
                  <span class="chip" [class]="'tone-' + tone(r.viewKey)">{{ r.viewLabel }}</span>
                  @if (p.progressPercent > 0) {
                    <span class="muted small">{{ p.progressPercent }}% done</span>
                  }
                </div>

                <div class="facts">
                  <div>
                    <span class="k">Responsible person</span>
                    <span class="v">{{ p.responsibleDisplayName ?? 'Not decided yet' }}</span>
                  </div>
                  @if (p.supportPeople.length) {
                    <div>
                      <span class="k">Support</span>
                      <span class="v">{{ p.supportPeople.join(', ') }}</span>
                    </div>
                  }
                  <div>
                    <span class="k">Quality check</span>
                    <span class="v">{{ p.qualityCheck }}</span>
                  </div>
                  @if (p.dueDate) {
                    <div>
                      <span class="k">Expected by</span>
                      <span class="v">{{ p.dueDate | date: 'mediumDate' }}</span>
                    </div>
                  }
                </div>

                @if (p.waitingReason) {
                  <p class="waiting">
                    <mat-icon>pause_circle</mat-icon>
                    <span>{{ p.waitingReason }}</span>
                  </p>
                }

                @if (p.latestUpdate) {
                  <div class="update">
                    <span class="k">Latest update</span>
                    <p class="body-text">{{ p.latestUpdate }}</p>
                    <span class="muted small">
                      {{ p.latestUpdateBy }} · {{ p.latestUpdateAt | date: 'MMM d, HH:mm' }}
                    </span>
                  </div>
                }
              </div>
            }

            <div class="card card-pad">
              <div class="row row-wrap chips">
                @if (!r.progress) {
                  <span class="chip" [class]="'tone-' + tone(r.viewKey)">{{ r.viewLabel }}</span>
                }
                <app-chip [value]="r.requestedUrgency" kind="urgency" />
                <span class="muted small">
                  Requested by {{ r.requestedByDisplayName }}
                  on {{ r.requestedAt | date: 'mediumDate' }}
                </span>
              </div>

              <h2 class="card-title top-gap">Description</h2>
              <p class="body-text">{{ r.description }}</p>

              @if (r.businessImpact) {
                <h2 class="card-title top-gap">Business impact</h2>
                <p class="body-text">{{ r.businessImpact }}</p>
              }
              @if (r.expectedResult) {
                <h2 class="card-title top-gap">Expected result</h2>
                <p class="body-text">{{ r.expectedResult }}</p>
              }
              @if (r.currentResult) {
                <h2 class="card-title top-gap">What happens instead</h2>
                <p class="body-text">{{ r.currentResult }}</p>
              }
              @if (r.reproductionSteps) {
                <h2 class="card-title top-gap">Steps to reproduce</h2>
                <p class="body-text">{{ r.reproductionSteps }}</p>
              }
            </div>

            <!--
              Questions & Replies. "Clarifications" is the workflow's word for it, not a word
              anyone uses; and an empty one gets a single line rather than a card of empty space —
              a section with nothing in it should take up nothing.
            -->
            <div class="card">
              <div class="card-pad">
                <h2 class="card-title" style="margin:0">Questions &amp; Replies</h2>
                @if (r.clarifications.length === 0) {
                  <p class="muted small" style="margin:6px 0 0">
                    No more information has been asked for.
                  </p>
                }
              </div>
              @if (r.clarifications.length > 0) {
                @for (c of r.clarifications; track c.id) {
                  <div class="clarification">
                    <div class="q">
                      <mat-icon>help</mat-icon>
                      <div>
                        <p class="body-text">{{ c.question }}</p>
                        <span class="muted small">{{ c.askedAt | date: 'MMM d, HH:mm' }}</span>
                      </div>
                    </div>

                    @if (c.answer) {
                      <div class="a">
                        <mat-icon>reply</mat-icon>
                        <div>
                          <p class="body-text">{{ c.answer }}</p>
                          <span class="muted small">{{ c.answeredAt | date: 'MMM d, HH:mm' }}</span>
                        </div>
                      </div>
                    } @else if (isRequester()) {
                      <div class="answer-box">
                        <mat-form-field class="full">
                          <mat-label>Your answer</mat-label>
                          <textarea matInput rows="2" [(ngModel)]="answers[c.id]"></textarea>
                        </mat-form-field>
                        <button matButton="filled" [disabled]="!answers[c.id]?.trim()"
                                (click)="answer(c.id)">Send answer</button>
                      </div>
                    } @else {
                      <p class="muted small pending">Waiting on the requester.</p>
                    }
                  </div>
                }
              }
            </div>

            <!--
              Attachments, with screenshots shown as screenshots. Looking at a picture should not
              require downloading it, finding it and opening something else.
            -->
            <div class="card card-pad">
              <div class="row" style="margin-bottom:12px">
                <h2 class="card-title" style="margin:0">Attachments</h2>
                <span class="spacer"></span>
                <button matButton (click)="file.click()">
                  <mat-icon>upload</mat-icon> Add a file
                </button>
                <input #file type="file" hidden (change)="upload($event)" />
              </div>
              <app-attachments [attachments]="r.attachments" />
            </div>

            <!-- --- history ------------------------------------------------------------------ -->
            @if (r.activity.length > 0) {
              <div class="card">
                <div class="card-pad"><h2 class="card-title" style="margin:0">History</h2></div>
                @for (a of r.activity; track a.id) {
                  <div class="event">
                    <mat-icon>history</mat-icon>
                    <div class="what">
                      <div>{{ a.description }}</div>
                      <div class="muted small">
                        {{ a.actorDisplayName }} · {{ a.occurredAt | date: 'MMM d, HH:mm' }}
                      </div>
                    </div>
                  </div>
                }
              </div>
            }
          </div>

          <!-- --- triage ---------------------------------------------------------------------- -->
          <aside class="stack">
            @if (canTriage()) {
              <div class="card card-pad">
                <h2 class="card-title">Your decision</h2>

                <mat-button-toggle-group [(ngModel)]="outcome" vertical class="outcomes">
                  <mat-button-toggle value="Approve">
                    <mat-icon>check_circle</mat-icon> Approve — create the task
                  </mat-button-toggle>
                  <mat-button-toggle value="RequestClarification">
                    <mat-icon>help</mat-icon> Ask for clarification
                  </mat-button-toggle>
                  <mat-button-toggle value="Reject">
                    <mat-icon>cancel</mat-icon> Reject
                  </mat-button-toggle>
                  <mat-button-toggle value="MarkDuplicate">
                    <mat-icon>content_copy</mat-icon> Duplicate
                  </mat-button-toggle>
                  <mat-button-toggle value="Defer">
                    <mat-icon>schedule</mat-icon> Defer
                  </mat-button-toggle>
                  <mat-button-toggle value="Escalate">
                    <mat-icon>priority_high</mat-icon> Escalate
                  </mat-button-toggle>
                </mat-button-toggle-group>

                @if (outcome === 'Approve') {
                  <div class="approve-fields">
                    <app-search-select class="full" label="Approved priority"
                                       [options]="priorityOptions" [(ngModel)]="priority" />

                    <mat-form-field class="full">
                      <mat-label>Estimate (hours)</mat-label>
                      <input matInput type="number" min="0" [(ngModel)]="estimate" />
                    </mat-form-field>

                    <mat-form-field class="full">
                      <mat-label>Acceptance criteria — one per line</mat-label>
                      <textarea matInput rows="4" [(ngModel)]="criteria"
                                placeholder="One per line — QC has to tick every one."></textarea>
                    </mat-form-field>
                  </div>
                } @else {
                  <mat-form-field class="full">
                    <mat-label>Reason (required unless approving)</mat-label>
                    <textarea matInput rows="3" [(ngModel)]="reason"></textarea>
                  </mat-form-field>
                }

                @if (outcome === 'MarkDuplicate') {
                  <mat-form-field class="full">
                    <mat-label>Duplicate of (request id)</mat-label>
                    <input matInput type="number" [(ngModel)]="duplicateOf" />
                  </mat-form-field>
                }

                <button matButton="filled" class="full submit"
                        [disabled]="!triageValid() || busy()" (click)="triage()">
                  {{ outcome === 'Approve' ? 'Approve and create task' : 'Record decision' }}
                </button>
              </div>
            }

            <div class="card card-pad">
              <h2 class="card-title">Details</h2>
              @if (r.clientName) {
                <app-field label="Client">
                  {{ r.clientName }}
                </app-field>
              }
              <app-field label="Type">{{ label(r.type) }}</app-field>
              <!--
                Shown to everyone, requester included. Unlike "Generated task" this is not the
                system talking about itself: they asked for eight things in one go, and this is how
                they get back to the other seven.
              -->
              @if (r.batchNumber) {
                <app-field label="Asked for with">
                  <a [routerLink]="['/requests/batches', r.batchId]">
                    {{ r.batchNumber }} — item {{ r.ordinalInBatch }} of {{ r.batchItemCount }}
                  </a>
                </app-field>
              }
              <app-field label="Needed by">
                {{ r.targetDate ? (r.targetDate | date: 'mediumDate') : '—' }}
              </app-field>
              <!--
                The internal record, for the people who work on it. A requester is not shown it at
                all: "Generated task" is the system talking about itself, and the progress panel
                already answered the question that link would have been clicked to answer.
              -->
              @if (canSeeTask()) {
                <app-field label="Generated task">
                  @if (r.generatedTaskId) {
                    <a [routerLink]="['/tasks', r.generatedTaskId]">View task</a>
                  } @else { Not approved yet }
                </app-field>
              }
            </div>
          </aside>
        </div>
      </div>
    }
  `,
  styles: `
    .event {
      display: flex; gap: 10px; align-items: flex-start;
      padding: 11px 20px; border-top: 1px solid var(--border);
    }
    .event mat-icon { font-size: 18px; width: 18px; height: 18px; color: var(--text-muted); }
    .event .what { min-width: 0; }
    .layout { display: grid; gap: 18px; grid-template-columns: minmax(0, 1fr) 340px; }
    @media (max-width: 1150px) { .layout { grid-template-columns: 1fr; } }
    .chips { gap: 9px; }
    .top-gap { margin-top: 18px; }
    .body-text { margin: 0; white-space: pre-wrap; line-height: 1.55; font-size: 14px; }
    .clarification { padding: 14px 20px; border-top: 1px solid var(--border); }
    .clarification:first-of-type { border-top: none; }
    .q, .a { display: flex; gap: 10px; padding: 4px 0; }
    .a { padding-left: 26px; }
    .q mat-icon { color: var(--tone-warn-fg); font-size: 18px; width: 18px; height: 18px; }
    .a mat-icon { color: var(--tone-good-fg); font-size: 18px; width: 18px; height: 18px; }
    .answer-box { padding: 8px 0 0 26px; }
    .pending { padding-left: 28px; }
    .attachment {
      display: flex; align-items: center; flex-wrap: wrap; gap: 10px;
      padding: 9px 20px; border-top: 1px solid var(--border);
    }
    .attachment mat-icon { color: var(--text-muted); }
    .full { width: 100%; }
    .progress-card { border-left: 3px solid #1d69d4; }
    .facts {
      display: grid; gap: 10px 24px; margin-top: 14px;
      grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
    }
    .facts .k, .update .k {
      display: block; font-size: 11.5px; text-transform: uppercase;
      letter-spacing: 0.04em; color: var(--text-muted); margin-bottom: 2px;
    }
    .facts .v { font-size: 14px; }
    .waiting {
      display: flex; align-items: flex-start; gap: 8px; margin: 14px 0 0;
      padding: 9px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-warn-bg); color: var(--tone-warn-fg);
    }
    .waiting mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
    .update { margin-top: 16px; padding-top: 14px; border-top: 1px solid var(--border); }
    .update p { margin: 0 0 4px; }
    .outcomes { width: 100%; margin-bottom: 14px; }
    .outcomes .mat-button-toggle { text-align: left; }
    .approve-fields { display: contents; }
    .submit { margin-top: 6px; }
  `,
})
export class RequestDetailComponent implements OnInit {
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  readonly id = input.required<string>();

  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly request = signal<RequestDetailDto | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);

  readonly priorities: Priority[] = ['Critical', 'High', 'Normal', 'Low'];
  readonly priorityOptions = enumOptions(this.priorities);
  readonly answers: Record<number, string> = {};

  outcome: TriageOutcome = 'Approve';
  priority: Priority = 'Normal';
  estimate: number | null = null;
  criteria = '';
  reason = '';
  duplicateOf: number | null = null;

  private requestId = 0;

  /**
   * The server is the authority on whether an edit is allowed — it refuses once triage has acted.
   * This mirrors the same rule so the button is not offered when it would only produce a refusal.
   */
  readonly canEdit = computed(() => {
    const r = this.request();
    return !!r && this.isRequester()
      && (r.status === 'Submitted' || r.status === 'ClarificationRequired');
  });

  edit(request: RequestDetailDto): void {
    this.dialog
      .open(RequestEditDialog, { data: { request } })
      .afterClosed()
      .subscribe((updated?: RequestDetailDto) => {
        // Already saved by the dialog, which stayed open if it had failed.
        if (!updated) return;
        this.request.set(updated);
        this.toast.success('Your changes have been saved and the reviewers told.');
      });
  }

  crumbs(r: RequestDetailDto): Crumb[] {
    const trail: Crumb[] = [{ label: 'Requests', route: '/requests' }, { label: r.requestNumber }];
    return trail;
  }

  readonly isRequester = computed(() =>
    this.request()?.requestedByUserId === this.auth.user()?.id);

  /** Triage only makes sense while the request is still open for a decision. */
  readonly canTriage = computed(() => {
    const r = this.request();
    return !!r && this.auth.has(Perm.taskReview)
      && ['Submitted', 'InReview', 'ClarificationRequired'].includes(r.status);
  });

  label = (value: string) => requestTypeLabel(value as RequestType);

  /**
   * Colour follows what the view means, not the internal status name — the reader is being shown
   * "Being Checked", so the chip has to be coloured on that basis.
   */
  tone(viewKey: string): string {
    switch (viewKey) {
      case 'done': return 'success';
      case 'declined': return 'danger';
      case 'input': case 'waiting': return 'warn';
      case 'working': case 'checking': return 'running';
      default: return 'neutral';
    }
  }

  /** Only people who act on tasks are offered the task. See the note in the template. */
  canSeeTask = () => this.auth.has(Perm.taskAssign) || this.auth.has(Perm.taskWork)
    || this.auth.has(Perm.taskReview) || this.auth.has(Perm.taskQCReview);

  ngOnInit(): void {
    this.requestId = Number(this.id());
    this.load();
  
    // Re-fetch on the server's say-so; see syncOn for why it debounces and tears down.
    syncOn<RequestChangedEvent>(
      [this.realtime.requestChanged],
      () => this.load(),
      this.destroyRef,
      { filter: (e) => e.requestId === this.requestId });
  }

  private load(): void {
    this.api.request(this.requestId).subscribe({
      next: (r) => {
        this.request.set(r);
        this.priority = (r.requestedUrgency as unknown as Priority) ?? 'Normal';
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  triageValid(): boolean {
    if (this.outcome === 'Approve') return true;
    if (this.outcome === 'MarkDuplicate' && !this.duplicateOf) return false;
    return this.reason.trim().length > 0;
  }

  triage(): void {
    this.busy.set(true);

    this.api.triage(this.requestId, {
      outcome: this.outcome,
      reason: this.reason.trim() || undefined,
      approvedPriority: this.outcome === 'Approve' ? this.priority : undefined,
      estimatedEffortHours: this.outcome === 'Approve' ? (this.estimate ?? undefined) : undefined,
      acceptanceCriteria: this.outcome === 'Approve' ? (this.criteria.trim() || undefined) : undefined,
      duplicateOfRequestId: this.duplicateOf ?? undefined,
    }).subscribe({
      next: (result) => {
        this.busy.set(false);
        this.reason = '';

        if (result.createdTaskId) {
          this.toast.success('Approved — the task has been created.');
          void this.router.navigate(['/tasks', result.createdTaskId]);
          return;
        }

        // The decision is not the request: re-fetch it rather than assigning the response over
        // the top, which is what left the page rendering undefined.
        this.load();
        this.toast.success('Decision saved.');
      },
      error: () => this.busy.set(false),
    });
  }

  answer(clarificationId: number): void {
    const text = this.answers[clarificationId]?.trim();
    if (!text) return;

    this.api.answerClarification(clarificationId, text).subscribe((updated) => {
      this.request.set(updated);
      this.answers[clarificationId] = '';
      this.toast.success('Answer sent — the request is back with the reviewer.');
    });
  }

  upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.api.uploadRequestAttachment(this.requestId, file).subscribe(() => {
      input.value = '';
      this.toast.success('File attached.');
      this.load();
    });
  }

  download(attachmentId: number, fileName: string): void {
    this.api.downloadAttachment(attachmentId).subscribe((blob) => saveBlob(blob, fileName));
  }

  size(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }
}
