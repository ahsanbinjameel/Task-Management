import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ApiService } from '../../../core/api.service';
import { AuthService } from '../../../core/auth.service';
import { ToastService } from '../../../core/toast.service';
import { Perm } from '../../../core/permissions';
import { AcceptanceCriterionDto, QCResult, QCReviewDto, TaskDetailDto } from '../../../core/models';
import { EmptyComponent } from '../../../shared/ui';

interface Verdict {
  index: number;
  text: string;
  met: boolean;
  note: string;
}

/**
 * The QC panel: the verdict form while a review is in progress, and the attempt history always.
 *
 * The criteria checklist is the point of the screen. A pass requires every criterion answered and
 * met — the server enforces it, and mirroring the rule here means the reviewer sees *which* box is
 * unticked instead of reading a validation error.
 */
@Component({
  selector: 'app-task-qc',
  standalone: true,
  imports: [
    DatePipe, FormsModule, MatButtonModule, MatButtonToggleModule, MatCheckboxModule,
    MatFormFieldModule, MatIconModule, MatInputModule, EmptyComponent,
  ],
  template: `
    <div class="stack">
      @if (canReview()) {
        <div class="card card-pad">
          <h2 class="card-title">QC attempt {{ nextAttempt() }}</h2>

          <mat-button-toggle-group [(ngModel)]="result" class="verdict">
            <mat-button-toggle value="Passed"><mat-icon>check</mat-icon> Pass</mat-button-toggle>
            <mat-button-toggle value="Failed"><mat-icon>close</mat-icon> Fail</mat-button-toggle>
            <mat-button-toggle value="ClarificationRequired">
              <mat-icon>help</mat-icon> Query
            </mat-button-toggle>
          </mat-button-toggle-group>

          @if (verdicts().length > 0) {
            <h3 class="sub">Acceptance criteria</h3>
            @for (v of verdicts(); track v.index) {
              <div class="criterion">
                <mat-checkbox [(ngModel)]="v.met">{{ v.text }}</mat-checkbox>
                @if (!v.met) {
                  <input class="note" placeholder="What is wrong with this one?" [(ngModel)]="v.note" />
                }
              </div>
            }
            @if (result === 'Passed' && unmet().length > 0) {
              <p class="warn small">
                <mat-icon>error_outline</mat-icon>
                Every criterion must be met before QC can pass. Unmet: {{ unmet().join(', ') }}.
              </p>
            }
          } @else {
            <p class="muted small">
              This task declares no acceptance criteria, so the verdict rests on your comments.
            </p>
          }

          <mat-form-field class="full">
            <mat-label>Comments{{ result === 'Passed' ? ' (optional)' : '' }}</mat-label>
            <textarea matInput rows="3" [(ngModel)]="comments"></textarea>
            @if (result !== 'Passed') {
              <mat-hint>Required — the assignee has to know what to fix.</mat-hint>
            }
          </mat-form-field>

          <div class="row row-wrap">
            <mat-form-field class="grow">
              <mat-label>Environment</mat-label>
              <input matInput [(ngModel)]="environment" placeholder="e.g. Staging" />
            </mat-form-field>
            <mat-form-field class="grow">
              <mat-label>Build version</mat-label>
              <input matInput [(ngModel)]="buildVersion" />
            </mat-form-field>
          </div>

          <div class="row">
            <span class="spacer"></span>
            <button matButton="filled" [disabled]="!valid() || busy()" (click)="submit()">
              Submit QC attempt
            </button>
          </div>
        </div>
      } @else if (task().status === 'CompletedReadyForQC') {
        <div class="card card-pad muted small">
          Waiting for a reviewer to pick this up.
          @if (task().primaryAssigneeUserId === auth.user()?.id) {
            You cannot QC your own work.
          }
        </div>
      }

      <div class="card">
        <div class="card-pad"><h2 class="card-title" style="margin:0">Attempt history</h2></div>
        @if (history().length === 0) {
          <app-empty message="No QC attempts yet" icon="verified" />
        } @else {
          @for (review of history(); track review.id) {
            <div class="attempt">
              <div class="row">
                <span class="chip" [class]="'tone-' + tone(review.result)">
                  Attempt {{ review.attemptNumber }} · {{ review.result }}
                </span>
                <span class="spacer"></span>
                <span class="muted small">
                  {{ review.reviewerDisplayName }} · {{ review.reviewedAt | date: 'MMM d, HH:mm' }}
                </span>
              </div>
              @if (review.comments) { <p class="comments">{{ review.comments }}</p> }
              @if (review.environment || review.buildVersion) {
                <p class="muted small">
                  {{ review.environment }}{{ review.environment && review.buildVersion ? ' · ' : '' }}{{ review.buildVersion }}
                </p>
              }
              @for (c of review.criteria; track c.index) {
                <div class="criterion-result small">
                  <mat-icon [class.met]="c.met === true" [class.unmet]="c.met === false">
                    {{ c.met === true ? 'check_circle' : c.met === false ? 'cancel' : 'radio_button_unchecked' }}
                  </mat-icon>
                  <span>{{ c.text }}</span>
                  @if (c.note) { <span class="muted">— {{ c.note }}</span> }
                </div>
              }
            </div>
          }
        }
      </div>
    </div>
  `,
  styles: `
    .verdict { margin-bottom: 16px; }
    .sub { font-size: 13px; font-weight: 600; margin: 4px 0 8px; }
    .criterion { padding: 5px 0; }
    .note {
      display: block; width: 100%; margin: 5px 0 0 32px; max-width: calc(100% - 32px);
      border: 1px solid var(--border-strong); border-radius: 7px; padding: 6px 9px; font: inherit;
      font-size: 13px;
    }
    .full { width: 100%; margin-top: 12px; }
    .grow { flex: 1 1 180px; }
    .warn {
      display: flex; align-items: center; gap: 6px; margin: 6px 0 0;
      color: var(--tone-danger-fg);
    }
    .warn mat-icon { font-size: 17px; width: 17px; height: 17px; }
    .attempt { padding: 14px 20px; border-top: 1px solid var(--border); }
    .comments { margin: 8px 0 4px; white-space: pre-wrap; font-size: 13.5px; }
    .criterion-result { display: flex; align-items: center; gap: 6px; padding: 2px 0; }
    .criterion-result mat-icon { font-size: 16px; width: 16px; height: 16px; color: var(--text-muted); }
    .criterion-result mat-icon.met { color: var(--tone-good-fg); }
    .criterion-result mat-icon.unmet { color: var(--tone-danger-fg); }
  `,
})
export class TaskQcComponent implements OnInit {
  readonly task = input.required<TaskDetailDto>();
  readonly changed = output<void>();

  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  readonly history = signal<QCReviewDto[]>([]);
  readonly verdicts = signal<Verdict[]>([]);
  readonly busy = signal(false);

  result: QCResult = 'Passed';
  comments = '';
  environment = '';
  buildVersion = '';

  readonly canReview = computed(() =>
    this.task().status === 'QCReview'
    && this.auth.has(Perm.taskQCReview)
    && this.task().qcUserId === this.auth.user()?.id);

  readonly nextAttempt = computed(() => this.history().length + 1);

  readonly unmet = computed(() =>
    this.verdicts().filter((v) => !v.met).map((v) => v.index + 1));

  ngOnInit(): void {
    this.history.set(this.task().qcReviews);

    this.api.acceptanceCriteria(this.task().id).subscribe((c) => {
      this.verdicts.set(c.criteria.map((criterion: AcceptanceCriterionDto) => ({
        index: criterion.index,
        text: criterion.text,
        met: criterion.met ?? false,
        note: criterion.note ?? '',
      })));
    });
  }

  tone = (result: QCResult) =>
    result === 'Passed' ? 'good' : result === 'Failed' ? 'danger' : 'warn';

  valid(): boolean {
    if (this.result !== 'Passed' && !this.comments.trim()) return false;
    if (this.result === 'Passed' && this.unmet().length > 0) return false;
    return true;
  }

  submit(): void {
    this.busy.set(true);

    this.api.submitQC(this.task().id, {
      result: this.result,
      comments: this.comments.trim() || undefined,
      environment: this.environment.trim() || undefined,
      buildVersion: this.buildVersion.trim() || undefined,
      criteria: this.verdicts().map((v) => ({
        index: v.index, met: v.met, note: v.note.trim() || undefined,
      })),
    }).subscribe({
      next: (task) => {
        this.busy.set(false);
        this.history.set(task.qcReviews);
        this.comments = '';
        this.toast.success(
          this.result === 'Passed' ? 'QC passed.'
          : this.result === 'Failed' ? 'Sent back for rework.'
          : 'Query recorded.');
        this.changed.emit();
      },
      error: () => this.busy.set(false),
    });
  }
}
