import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { Perm } from '../../core/permissions';
import { humanizeEnum, saveBlob } from '../../core/format';
import { Priority, RequestDetailDto, TriageOutcome } from '../../core/models';
import {
  ChipComponent, EmptyComponent, FieldComponent, LoadingComponent, PageHeaderComponent,
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
    DatePipe, FormsModule, RouterLink, MatButtonModule, MatButtonToggleModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatSelectModule, MatTooltipModule,
    PageHeaderComponent, ChipComponent, FieldComponent, LoadingComponent, EmptyComponent,
  ],
  template: `
    @if (loading()) {
      <app-loading message="Loading request…" />
    } @else if (request(); as r) {
      <div class="page">
        <app-page-header [title]="r.title" [subtitle]="r.requestNumber + ' · ' + label(r.type)">
          @if (r.generatedTaskId) {
            <a matButton="filled" [routerLink]="['/tasks', r.generatedTaskId]">
              <mat-icon>task_alt</mat-icon> Open the task
            </a>
          }
        </app-page-header>

        <div class="layout">
          <div class="stack">
            <div class="card card-pad">
              <div class="row row-wrap chips">
                <app-chip [value]="r.status" />
                <app-chip [value]="r.requestedUrgency" kind="priority" />
                <span class="muted small">
                  Raised by {{ r.requestedByDisplayName }} on {{ r.requestedAt | date: 'mediumDate' }}
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

            <!-- --- clarifications ------------------------------------------------------------ -->
            <div class="card">
              <div class="card-pad"><h2 class="card-title" style="margin:0">Clarifications</h2></div>
              @if (r.clarifications.length === 0) {
                <app-empty message="Nothing has been queried" icon="help_outline" />
              } @else {
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
                          <mat-hint>Answering sends the request back to review.</mat-hint>
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

            <!-- --- attachments --------------------------------------------------------------- -->
            <div class="card">
              <div class="card-pad row">
                <h2 class="card-title" style="margin:0">Attachments</h2>
                <span class="spacer"></span>
                <button matButton (click)="file.click()">
                  <mat-icon>upload</mat-icon> Upload
                </button>
                <input #file type="file" hidden (change)="upload($event)" />
              </div>
              @if (r.attachments.length === 0) {
                <app-empty message="No files attached" icon="attach_file" />
              } @else {
                @for (a of r.attachments; track a.id) {
                  <div class="attachment">
                    <mat-icon>description</mat-icon>
                    <span class="truncate">{{ a.fileName }}</span>
                    <span class="muted small nowrap">{{ size(a.sizeBytes) }}</span>
                    <span class="spacer"></span>
                    <button matButton (click)="download(a.id, a.fileName)">Download</button>
                  </div>
                }
              }
            </div>
          </div>

          <!-- --- triage ---------------------------------------------------------------------- -->
          <aside class="stack">
            @if (canTriage()) {
              <div class="card card-pad">
                <h2 class="card-title">Triage</h2>

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
                    <mat-form-field class="full">
                      <mat-label>Approved priority</mat-label>
                      <mat-select [(ngModel)]="priority">
                        @for (p of priorities; track p) { <mat-option [value]="p">{{ p }}</mat-option> }
                      </mat-select>
                      <mat-hint>This, not the requested urgency, schedules the work.</mat-hint>
                    </mat-form-field>

                    <mat-form-field class="full">
                      <mat-label>Estimate (hours)</mat-label>
                      <input matInput type="number" min="0" [(ngModel)]="estimate" />
                    </mat-form-field>

                    <mat-form-field class="full">
                      <mat-label>Acceptance criteria</mat-label>
                      <textarea matInput rows="4" [(ngModel)]="criteria"
                                placeholder="One per line — QC has to tick every one."></textarea>
                      <mat-hint>One criterion per line.</mat-hint>
                    </mat-form-field>
                  </div>
                } @else {
                  <mat-form-field class="full">
                    <mat-label>Reason</mat-label>
                    <textarea matInput rows="3" [(ngModel)]="reason"></textarea>
                    <mat-hint>Required for everything except approval.</mat-hint>
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
              <app-field label="Type">{{ label(r.type) }}</app-field>
              <app-field label="Needed by">
                {{ r.targetDate ? (r.targetDate | date: 'mediumDate') : '—' }}
              </app-field>
              <app-field label="Generated task">
                @if (r.generatedTaskId) {
                  <a [routerLink]="['/tasks', r.generatedTaskId]">View task</a>
                } @else { Not approved yet }
              </app-field>
            </div>
          </aside>
        </div>
      </div>
    }
  `,
  styles: `
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
      display: flex; align-items: center; gap: 10px;
      padding: 9px 20px; border-top: 1px solid var(--border);
    }
    .attachment mat-icon { color: var(--text-muted); }
    .full { width: 100%; }
    .outcomes { width: 100%; margin-bottom: 14px; }
    .outcomes .mat-button-toggle { text-align: left; }
    .approve-fields { display: contents; }
    .submit { margin-top: 6px; }
  `,
})
export class RequestDetailComponent implements OnInit {
  readonly id = input.required<string>();

  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly request = signal<RequestDetailDto | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);

  readonly priorities: Priority[] = ['Critical', 'High', 'Normal', 'Low'];
  readonly answers: Record<number, string> = {};

  outcome: TriageOutcome = 'Approve';
  priority: Priority = 'Normal';
  estimate: number | null = null;
  criteria = '';
  reason = '';
  duplicateOf: number | null = null;

  private requestId = 0;

  readonly isRequester = computed(() =>
    this.request()?.requestedByUserId === this.auth.user()?.id);

  /** Triage only makes sense while the request is still open for a decision. */
  readonly canTriage = computed(() => {
    const r = this.request();
    return !!r && this.auth.has(Perm.taskReview)
      && ['Submitted', 'InReview', 'ClarificationRequired'].includes(r.status);
  });

  label = (value: string) => humanizeEnum(value);

  ngOnInit(): void {
    this.requestId = Number(this.id());
    this.load();
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
      next: (updated) => {
        this.busy.set(false);
        this.request.set(updated);
        this.reason = '';

        if (updated.generatedTaskId) {
          this.toast.success('Approved — the task has been created.');
          void this.router.navigate(['/tasks', updated.generatedTaskId]);
        } else {
          this.toast.success('Decision recorded.');
        }
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
