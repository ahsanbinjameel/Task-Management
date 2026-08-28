import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpContext } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ApiService } from '../../../core/api.service';
import { AuthService } from '../../../core/auth.service';
import { ToastService } from '../../../core/toast.service';
import { Perm } from '../../../core/permissions';
import {
  AcceptanceCriterionDto, AttachmentDto, QCResult, QCReviewDto, TaskDetailDto,
} from '../../../core/models';
import { EmptyComponent } from '../../../shared/ui';
import { ConfirmDialog, ConfirmData } from '../../../shared/dialogs';
import { AttachmentsComponent } from '../../../shared/attachments.component';
import { AttachmentUploadComponent } from '../../../shared/attachment-upload.component';

/** What the reviewer picked for one item. `undefined` means they have not answered it yet. */
type Answer = 'pass' | 'fail' | 'na' | undefined;

interface Verdict {
  index: number;
  text: string;
  answer: Answer;
  note: string;
}

/** The wire format: true passed, false failed, null not applicable. */
const toMet = (answer: Answer): boolean | null =>
  answer === 'pass' ? true : answer === 'fail' ? false : null;

const fromMet = (met: boolean | null | undefined): Answer =>
  met === true ? 'pass' : met === false ? 'fail' : met === null ? 'na' : undefined;

/**
 * The quality check panel: the check form while a review is in progress, and the history always.
 *
 * Two things matter here.
 *
 * **Verdicts are replaced, never mutated.** An earlier version bound `[(ngModel)]` straight onto
 * objects held inside a signal's array. Mutating an object inside the array does not change the
 * array reference, so the signal never notified and the `computed` that validated the form stayed
 * frozen at its initial "nothing answered" value — the warning claimed every item was unmet however
 * many were ticked, and the submit button never enabled. Every change now goes through
 * `setAnswer`/`setNote`, which write a new array.
 *
 * **Each item is three-way, not a checkbox.** "Does not apply to this work" is a real answer and
 * has to be distinguishable from "not looked at yet"; only an explicit Fail blocks a pass.
 */
@Component({
  selector: 'app-task-qc',
  standalone: true,
  imports: [
    DatePipe, FormsModule, MatButtonModule, MatButtonToggleModule,
    MatFormFieldModule, MatIconModule, MatInputModule, EmptyComponent,
    AttachmentsComponent, AttachmentUploadComponent,
  ],
  template: `
    <div class="stack">
      @if (canReview()) {
        <div class="card card-pad">
          <h2 class="card-title">Quality check</h2>

          @if (verdicts().length > 0) {
            <h3 class="sub">What needs to be checked</h3>
            <p class="muted small">
              Mark each item. Choose <strong>N/A</strong> if it does not apply to this piece of work.
            </p>

            @for (v of verdicts(); track v.index) {
              <div class="criterion" [class.answered]="v.answer !== undefined">
                <div class="criterion-text">
                  <span class="num">{{ v.index + 1 }}</span>
                  <span>{{ v.text }}</span>
                </div>
                <mat-button-toggle-group
                  [ngModel]="v.answer"
                  (ngModelChange)="setAnswer(v.index, $event)"
                  class="answers"
                  aria-label="Result for this item">
                  <mat-button-toggle value="pass">Pass</mat-button-toggle>
                  <mat-button-toggle value="fail">Fail</mat-button-toggle>
                  <mat-button-toggle value="na">N/A</mat-button-toggle>
                </mat-button-toggle-group>
                @if (v.answer === 'fail') {
                  <input
                    class="note"
                    placeholder="Why it failed"
                    [ngModel]="v.note"
                    (ngModelChange)="setNote(v.index, $event)" />
                }
              </div>
            }

            @if (unanswered().length > 0) {
              <p class="hint small">
                <mat-icon>info</mat-icon>
                {{ unanswered().length }} item{{ unanswered().length === 1 ? '' : 's' }} still to
                answer.
              </p>
            }
          } @else {
            <p class="muted small">
              No specific items were listed for this task, so your comments below are the record of
              what you checked.
            </p>
          }

          <h3 class="sub">Result</h3>
          <mat-button-toggle-group [ngModel]="result()" (ngModelChange)="setResult($event)" class="verdict">
            <mat-button-toggle value="Passed"><mat-icon>check</mat-icon> Passed</mat-button-toggle>
            <mat-button-toggle value="Failed"><mat-icon>build</mat-icon> Needs fixing</mat-button-toggle>
            <mat-button-toggle value="ClarificationRequired">
              <mat-icon>help</mat-icon> Need information
            </mat-button-toggle>
          </mat-button-toggle-group>
          <p class="muted small explain">{{ explanation() }}</p>

          <mat-form-field class="full">
            <mat-label>{{ commentLabel() }}</mat-label>
            <textarea matInput rows="3" [ngModel]="comments()" (ngModelChange)="comments.set($event)"></textarea>
          </mat-form-field>

          <div class="row row-wrap">
            <mat-form-field class="grow">
              <mat-label>Where you checked it (optional)</mat-label>
              <input matInput [ngModel]="environment()" (ngModelChange)="environment.set($event)" />
            </mat-form-field>
            <mat-form-field class="grow">
              <mat-label>Version (optional)</mat-label>
              <input matInput [ngModel]="buildVersion()" (ngModelChange)="buildVersion.set($event)" />
            </mat-form-field>
          </div>

          <h3 class="sub">What you saw</h3>
          <p class="muted small">
            Screenshots of the check itself. They stay with this attempt, so a later one passing
            does not erase what was wrong with this one.
          </p>
          <app-attachments [attachments]="evidence()" emptyText="Nothing attached yet." />
          <app-attachment-upload
            [taskId]="task().id" kind="QCEvidence"
            label="Attach a screenshot" icon="photo_camera"
            (uploaded)="evidenceAdded($event)" />

          @if (blockedReason(); as reason) {
            <p class="warn small"><mat-icon>error_outline</mat-icon> {{ reason }}</p>
          }

          <div class="row">
            <span class="spacer"></span>
            <button matButton="filled" [disabled]="!!blockedReason() || busy()" (click)="submit()">
              {{ submitLabel() }}
            </button>
          </div>
        </div>
      } @else if (task().status === 'CompletedReadyForQC') {
        <div class="card card-pad muted small">
          Waiting for someone to start the quality check.
          @if (task().primaryAssigneeUserId === auth.user()?.id) {
            You cannot check your own work.
          }
        </div>
      }

      <div class="card">
        <div class="card-pad"><h2 class="card-title" style="margin:0">Previous checks</h2></div>
        @if (history().length === 0) {
          <app-empty message="This task has not been checked yet" icon="verified" />
        } @else {
          @for (review of history(); track review.id) {
            <div class="attempt">
              <div class="row">
                <span class="chip" [class]="'tone-' + tone(review.result)">
                  Check {{ review.attemptNumber }} · {{ resultLabel(review.result) }}
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
              @if (review.attachments?.length) {
                <div class="evidence">
                  <app-attachments [attachments]="review.attachments ?? []" />
                </div>
              }
              @for (c of review.criteria; track c.index) {
                <div class="criterion-result small">
                  <mat-icon [class.met]="c.met === true" [class.unmet]="c.met === false">
                    {{ c.met === true ? 'check_circle' : c.met === false ? 'cancel' : 'remove_circle_outline' }}
                  </mat-icon>
                  <span>{{ c.text }}</span>
                  @if (c.met === null) { <span class="muted">— not applicable</span> }
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
    .intro { margin: -4px 0 14px; }
    .verdict { margin-bottom: 4px; }
    .explain { margin: 0 0 4px; }
    .sub { font-size: 13px; font-weight: 600; margin: 18px 0 4px; }
    .criterion {
      display: flex; flex-wrap: wrap; align-items: center; gap: 10px;
      padding: 10px 0; border-bottom: 1px solid var(--border);
    }
    .criterion-text { display: flex; gap: 9px; flex: 1 1 240px; min-width: 0; line-height: 1.45; }
    .criterion-text span { overflow-wrap: anywhere; }
    .num {
      flex: none; width: 21px; height: 21px; border-radius: 50%; font-size: 12px;
      display: inline-flex; align-items: center; justify-content: center;
      background: var(--surface-2); color: var(--text-muted); font-weight: 600;
    }
    .answers { flex: none; }
    .note {
      flex: 1 1 100%; width: 100%; border: 1px solid var(--border-strong); border-radius: 7px;
      padding: 6px 9px; font: inherit; font-size: 13px;
    }
    .full { width: 100%; margin-top: 12px; }
    .grow { flex: 1 1 180px; }
    .hint, .warn { display: flex; align-items: center; gap: 6px; margin: 10px 0 0; }
    .hint { color: var(--text-muted); }
    .warn { color: var(--tone-danger-fg); }
    .hint mat-icon, .warn mat-icon { font-size: 17px; width: 17px; height: 17px; }
    .attempt { padding: 14px 20px; border-top: 1px solid var(--border); }
    .evidence { margin: 10px 0 6px; }
    .comments { margin: 8px 0 4px; white-space: pre-wrap; font-size: 13.5px; }
    .criterion-result { display: flex; align-items: center; gap: 6px; padding: 2px 0; }
    .criterion-result mat-icon { font-size: 16px; width: 16px; height: 16px; color: var(--text-muted); }
    .criterion-result mat-icon.met { color: var(--tone-good-fg); }
    .criterion-result mat-icon.unmet { color: var(--tone-danger-fg); }

    @media (max-width: 560px) {
      .criterion { align-items: flex-start; }
      .answers { width: 100%; }
      .answers ::ng-deep .mat-button-toggle { flex: 1 1 0; }
    }
  `,
})
export class TaskQcComponent implements OnInit {
  readonly task = input.required<TaskDetailDto>();
  readonly changed = output<void>();

  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly dialog = inject(MatDialog);

  readonly history = signal<QCReviewDto[]>([]);
  readonly verdicts = signal<Verdict[]>([]);
  readonly busy = signal(false);

  /**
   * Evidence uploaded while the check is being written up.
   *
   * It goes to the server straight away and waits there unclaimed, because the attempt it belongs
   * to does not exist until the verdict is recorded — the verdict adopts whatever this checker
   * staged. A verdict the server refuses therefore leaves the files where they are, ready for the
   * corrected submission, instead of making somebody find the screenshots again.
   */
  readonly evidence = signal<AttachmentDto[]>([]);

  readonly result = signal<QCResult>('Passed');
  readonly comments = signal('');
  readonly environment = signal('');
  readonly buildVersion = signal('');

  readonly canReview = computed(() =>
    this.task().status === 'QCReview'
    && this.auth.has(Perm.taskQCReview)
    && this.task().qcUserId === this.auth.user()?.id);

  readonly unanswered = computed(() =>
    this.verdicts().filter((v) => v.answer === undefined).map((v) => v.index + 1));

  readonly failed = computed(() =>
    this.verdicts().filter((v) => v.answer === 'fail').map((v) => v.index + 1));

  readonly explanation = computed(() => {
    switch (this.result()) {
      case 'Passed':
        return 'Everything required works. The task can move on towards being closed.';
      case 'Failed':
        return 'Something does not work. The task goes back to be fixed, and your comments say what to fix.';
      default:
        return 'You cannot decide yet — something is unclear or missing. Say what you need below.';
    }
  });

  readonly commentLabel = computed(() => {
    switch (this.result()) {
      case 'Passed': return 'Comments (optional)';
      case 'Failed': return 'What needs to be fixed?';
      default: return 'What information do you need?';
    }
  });

  readonly submitLabel = computed(() => {
    switch (this.result()) {
      case 'Passed': return 'Record as passed';
      case 'Failed': return 'Send back for fixing';
      default: return 'Ask for information';
    }
  });

  /**
   * One place that decides whether the form can be sent, and says why not in plain words. The
   * button's disabled state and the warning line read from the same value so they cannot disagree.
   */
  readonly blockedReason = computed<string | null>(() => {
    if (this.result() !== 'Passed') {
      return this.comments().trim()
        ? null
        : this.result() === 'Failed'
          ? 'Please say what needs to be fixed before sending this back.'
          : 'Please say what information you need.';
    }

    const unanswered = this.unanswered();
    if (unanswered.length > 0) {
      return `Please answer every item before recording a pass. Still to answer: ${unanswered.join(', ')}.`;
    }

    const failed = this.failed();
    if (failed.length > 0) {
      return `Item ${failed.join(', ')} is marked as failed, so this check cannot pass. `
        + 'Choose "Needs fixing" instead, or change that item to Pass.';
    }

    return null;
  });

  ngOnInit(): void {
    this.history.set(this.task().qcReviews);

    this.api.acceptanceCriteria(this.task().id).subscribe((c) => {
      this.verdicts.set(c.criteria.map((criterion: AcceptanceCriterionDto) => ({
        index: criterion.index,
        text: criterion.text,
        answer: fromMet(criterion.met),
        note: criterion.note ?? '',
      })));
    });
  }

  /** Replace, never mutate — see the class comment. */
  setAnswer(index: number, answer: Answer): void {
    this.verdicts.update((list) =>
      list.map((v) => (v.index === index ? { ...v, answer } : v)));
  }

  setNote(index: number, note: string): void {
    this.verdicts.update((list) =>
      list.map((v) => (v.index === index ? { ...v, note } : v)));
  }

  setResult(result: QCResult): void {
    this.result.set(result);
  }

  evidenceAdded(attachment: AttachmentDto): void {
    this.evidence.update((all) => [...all, attachment]);
  }

  resultLabel = (result: QCResult) =>
    result === 'Passed' ? 'Passed' : result === 'Failed' ? 'Needs fixing' : 'Need information';

  tone = (result: QCResult) =>
    result === 'Passed' ? 'good' : result === 'Failed' ? 'danger' : 'warn';

  /**
   * A verdict is a numbered attempt, and attempts are append-only — there is no edit and no
   * withdraw, and the next look is attempt N+1 sitting underneath this one in the history. Both
   * verdicts that move the task get a question first.
   *
   * "Need information" does not: it deliberately leaves the task in QC (a query is not a lifecycle
   * state), so the checker can simply ask again.
   */
  private verdictConfirmation(): { title: string; message: string; confirmText: string; danger?: boolean } | null {
    if (this.result() === 'Passed') {
      return {
        title: 'Record a pass?',
        message:
          'This clears the task for closure and cannot be withdrawn — a pass you change your mind '
          + 'about has to be followed by reopening the task, which needs a fresh check afterwards.',
        confirmText: 'Record the pass',
      };
    }

    if (this.result() === 'Failed') {
      return {
        title: 'Send this back to be fixed?',
        message:
          'The task goes back to the person who did the work, with your notes and screenshots '
          + 'attached to this attempt. The attempt is kept whatever happens next, so it cannot be '
          + 'taken back.',
        confirmText: 'Send it back',
        danger: true,
      };
    }

    return null;
  }

  submit(): void {
    if (this.blockedReason()) return;

    const confirmation = this.verdictConfirmation();

    if (!confirmation) {
      this.busy.set(true);
      this.submitVerdict().subscribe({
        next: (task) => { this.busy.set(false); this.afterVerdict(task); },
        error: () => this.busy.set(false),
      });
      return;
    }

    // Submitting from inside the dialog is what keeps a refused verdict from throwing away the
    // notes and the per-criterion answers the checker just worked through.
    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: { ...confirmation, submit: (ctx: HttpContext) => this.submitVerdict(ctx) },
      })
      .afterClosed()
      .subscribe((task?: unknown) => {
        if (!task) return;
        this.afterVerdict(task as TaskDetailDto);
      });
  }

  private submitVerdict(context?: HttpContext) {
    return this.api.submitQC(this.task().id, {
      result: this.result(),
      comments: this.comments().trim() || undefined,
      environment: this.environment().trim() || undefined,
      buildVersion: this.buildVersion().trim() || undefined,
      // Only send items the reviewer actually answered. A missing entry means "not answered",
      // which the server treats differently from "not applicable".
      criteria: this.verdicts()
        .filter((v) => v.answer !== undefined)
        .map((v) => ({
          index: v.index,
          met: toMet(v.answer),
          note: v.note.trim() || undefined,
        })),
    }, context);
  }

  private afterVerdict(task: TaskDetailDto): void {
    this.history.set(task.qcReviews);
    this.comments.set('');
    // The attempt has adopted them; they are now part of its record above.
    this.evidence.set([]);
    this.toast.success(
      this.result() === 'Passed' ? 'Quality check passed.'
      : this.result() === 'Failed' ? 'Sent back to be fixed.'
      : 'Your question has been recorded.');
    this.changed.emit();
  }
}
