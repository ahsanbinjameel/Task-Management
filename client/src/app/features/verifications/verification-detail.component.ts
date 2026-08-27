import { DestroyRef, Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDialog } from '@angular/material/dialog';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { RealtimeService } from '../../core/realtime.service';
import { syncOn } from '../../core/realtime-sync';
import { Perm } from '../../core/permissions';
import { FormSubmit } from '../../core/form-submit';
import { VerificationDetailDto, VerificationResult } from '../../core/models';
import { verificationResultLabel, verificationTargetLabel } from '../../core/labels';
import { ChipComponent, FieldComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';
import { SearchSelectComponent, SelectOption, enumOptions } from '../../shared/search-select.component';
import { AttachmentsComponent } from '../../shared/attachments.component';
import { AttachmentUploadComponent } from '../../shared/attachment-upload.component';
import { ConfirmDialog, ConfirmData, ReasonDialog, ReasonData } from '../../shared/dialogs';
import { VerificationAssignDialog } from './verification-assign-dialog.component';

/**
 * One check, and the place it is carried out.
 *
 * The panel that matters is "What did you find?". It is shown only to the assigned checker — the
 * server enforces that too, because the answer depends on the record rather than on the caller's
 * permissions — and it insists on findings, because a verdict with no account of how it was reached
 * leaves the reviewer exactly where they started.
 *
 * The sentence under the result picker is doing real work: it tells the checker that confirming a
 * problem does not create anything. Without it the obvious reading of "Problem confirmed" is "and
 * now it is scheduled", which is precisely what this feature exists not to do.
 */
@Component({
  selector: 'app-verification-detail',
  standalone: true,
  imports: [
    DatePipe, RouterLink, FormsModule, MatButtonModule, MatIconModule, MatFormFieldModule,
    MatInputModule,
    PageHeaderComponent, LoadingComponent, ChipComponent, FieldComponent,
    SearchSelectComponent, AttachmentsComponent, AttachmentUploadComponent,
  ],
  template: `
    @if (loading()) {
      <div class="page"><app-loading /></div>
    } @else if (verification(); as v) {
      <div class="page">
        <app-page-header [title]="v.verificationNumber + ' · ' + v.title" [subtitle]="v.targetSummary">
          <!--
            The action that matters most sits first and is the filled one. For an unclaimed check
            that is picking it up: a check nobody holds cannot move, and the notification telling
            people it needs a checker leads straight here.
          -->
          @if (canClaim()) {
            <button matButton="filled" (click)="claim()">
              <mat-icon>pan_tool_alt</mat-icon> Take this check
            </button>
          }
          @if (canStart()) {
            <button matButton="filled" (click)="start()">
              <mat-icon>play_arrow</mat-icon> Start checking
            </button>
          }
          @if (canAssign()) {
            <button matButton (click)="assign()">
              <mat-icon>person_add</mat-icon>
              {{ v.assignedToUserId ? 'Move it' : 'Assign a checker' }}
            </button>
          }
          @if (canCancel()) {
            <button matButton (click)="cancel()"><mat-icon>block</mat-icon> Call it off</button>
          }
        </app-page-header>

        <div class="cols">
          <div class="col-main">
            <div class="card card-pad">
              <div class="chips">
                <!-- The raw enum, not the label: the chip looks up both the words and the tone. -->
                <app-chip [value]="v.status" kind="verificationStatus" />
                <app-chip [value]="v.priority" kind="priority" />
                @if (v.result) { <app-chip [value]="v.result" kind="verificationResult" /> }
              </div>

              <div class="fields">
                <app-field label="What is being checked">{{ v.targetSummary }}</app-field>
                <app-field label="Kind">{{ targetLabel(v) }}</app-field>
                <app-field label="Raised by">{{ v.requestedByDisplayName }}</app-field>
                <app-field label="Checker">{{ v.assignedToDisplayName ?? 'Nobody yet' }}</app-field>
                @if (v.expectedBehavior) {
                  <app-field label="What it should do">{{ v.expectedBehavior }}</app-field>
                }
                @if (v.instructions) {
                  <app-field label="Instructions">{{ v.instructions }}</app-field>
                }
              </div>

              @if (v.requestId) {
                <p class="from">
                  Raised from request
                  <a [routerLink]="['/requests', v.requestId]">{{ v.requestNumber }}</a>
                  — {{ v.requestTitle }}
                </p>
              }

              <!--
                Said out loud rather than left to be inferred from "Nobody yet" in the field above.
                An unassigned check is the one state where the screen looks finished but nothing can
                happen, and the request behind it is sitting in review waiting on an answer.
              -->
              @if (v.status === 'Requested') {
                <p class="waiting">
                  <mat-icon>hourglass_empty</mat-icon>
                  <span>
                    Nobody is on this yet, so nothing is happening.
                    @if (canClaim()) { Take it to start, or assign it to someone else. }
                    @else if (canAssign()) { Assign a checker to get it moving. }
                    @else { It is waiting for a checker to pick it up. }
                  </span>
                </p>
              }
            </div>

            <!-- The outcome, once there is one. -->
            @if (v.status === 'Completed') {
              <div class="card card-pad outcome">
                <h3>{{ v.resultLabel }}</h3>
                <p class="findings">{{ v.findings }}</p>
                @if (v.requestId) {
                  <p class="muted small">
                    {{ v.requestNumber }} has gone back to whoever reviews it. Nothing has been
                    scheduled — approving it is still their decision to make.
                  </p>
                }
              </div>
            } @else if (v.status === 'Cancelled') {
              <div class="card card-pad outcome">
                <h3>Called off</h3>
                <p class="findings">{{ v.cancellationReason }}</p>
              </div>
            }

            <!-- The checker's own panel. -->
            @if (canReport()) {
              <div class="card card-pad">
                <h3>What did you find?</h3>

                @if (form.message(); as m) {
                  <div class="form-error" role="alert">
                    <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
                  </div>
                }

                <div class="stack">
                  <app-search-select label="Outcome" name="result"
                                     [options]="resultOptions" [(ngModel)]="result" />

                  <p class="note">{{ resultNote() }}</p>

                  <mat-form-field>
                    <mat-label>What you found</mat-label>
                    <textarea matInput rows="5" name="findings" [(ngModel)]="findings"
                              maxlength="8000"
                              (input)="form.clearField('findings')"></textarea>
                    @if (form.fieldError('findings'); as e) { <mat-error>{{ e }}</mat-error> }
                  </mat-form-field>

                  <div class="actions">
                    <button matButton="filled" [disabled]="!findings.trim() || form.busy()"
                            (click)="report()">
                      {{ form.busy() ? 'Recording…' : 'Record what you found' }}
                    </button>
                  </div>
                </div>
              </div>
            }
          </div>

          <div class="col-side">
            <div class="card card-pad">
              <h3>Evidence</h3>
              <!--
                app-attachments carries its own empty state, so a second "Nothing attached" above
                it was the same sentence twice. Its wording is passed in instead.
              -->
              <app-attachments
                [attachments]="v.attachments"
                emptyText="No evidence attached yet" />
              @if (canReport()) {
                <app-attachment-upload
                  [verificationId]="v.id"
                  label="Add evidence"
                  icon="add_a_photo"
                  (uploaded)="load()" />
              }
            </div>

            <div class="card card-pad">
              <h3>History</h3>
              <ol class="timeline">
                @for (entry of v.activity; track entry.id) {
                  <li>
                    <span class="who">{{ entry.actorDisplayName }}</span>
                    <span class="what">{{ entry.description }}</span>
                    <span class="when muted small">{{ entry.occurredAt | date: 'd MMM, HH:mm' }}</span>
                  </li>
                }
              </ol>
            </div>
          </div>
        </div>
      </div>
    }
  `,
  styles: `
    .cols { display: grid; grid-template-columns: minmax(0, 1fr) 340px; gap: 14px; }
    @media (max-width: 1100px) { .cols { grid-template-columns: minmax(0, 1fr); } }
    .col-main, .col-side { display: flex; flex-direction: column; gap: 14px; min-width: 0; }
    .chips { display: flex; gap: 8px; margin-bottom: 12px; flex-wrap: wrap; }
    .fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 12px; }
    .from { margin: 14px 0 0; font-size: 13.5px; }
    .waiting {
      display: flex; align-items: flex-start; gap: 8px; margin: 14px 0 0;
      padding: 10px 12px; border-radius: 8px; font-size: 13px; line-height: 1.5;
      background: var(--tone-warn-bg); color: var(--tone-warn-fg);
    }
    .waiting mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
    .outcome h3 { margin: 0 0 8px; }
    .findings { margin: 0 0 10px; white-space: pre-wrap; line-height: 1.55; }
    h3 { font-size: 14px; margin: 0 0 12px; }
    .stack { display: flex; flex-direction: column; gap: 10px; }
    .stack mat-form-field, .stack app-search-select { margin: 0; width: 100%; }
    .note {
      margin: 0; padding: 9px 11px; border-radius: 8px;
      background: var(--tone-running-bg); font-size: 13px; line-height: 1.5;
    }
    .actions { display: flex; justify-content: flex-end; }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
    .timeline { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
    .timeline li { display: flex; flex-direction: column; gap: 1px; font-size: 13px; line-height: 1.45; }
    .who { font-weight: 600; }
  `,
})
export class VerificationDetailComponent implements OnInit, OnDestroy {
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly dialog = inject(MatDialog);
  private readonly realtime = inject(RealtimeService);

  readonly form = new FormSubmit();
  readonly loading = signal(true);
  readonly verification = signal<VerificationDetailDto | null>(null);

  result: VerificationResult = 'IssueConfirmed';
  findings = '';

  private id = 0;

  readonly resultOptions: SelectOption[] = enumOptions<VerificationResult>(
    ['IssueConfirmed', 'WorkingCorrectly', 'ConfigurationOrDataIssue', 'NeedsClarification',
     'Inconclusive'],
    verificationResultLabel);

  /** Whether the signed-in person is the checker this was given to. */
  private readonly isChecker = computed(() =>
    this.verification()?.assignedToUserId === this.auth.user()?.id);

  readonly canStart = computed(() =>
    this.isChecker() && this.verification()?.status === 'Assigned'
    && this.auth.has(Perm.verificationWork));

  /**
   * Picking up a check nobody holds. Deliberately only for `Requested`: once somebody has it,
   * moving it is a decision about two people's workloads and goes through assignment, which asks
   * why. The server enforces the same rule.
   */
  readonly canClaim = computed(() =>
    this.verification()?.status === 'Requested'
    && this.verification()?.assignedToUserId == null
    && this.auth.has(Perm.verificationWork));

  readonly canAssign = computed(() => {
    const status = this.verification()?.status;
    return this.auth.has(Perm.verificationCreate)
      && status !== 'Completed' && status !== 'Cancelled';
  });

  readonly canReport = computed(() => {
    const status = this.verification()?.status;
    return this.isChecker() && this.auth.has(Perm.verificationWork)
      && (status === 'Assigned' || status === 'InProgress');
  });

  readonly canCancel = computed(() => {
    const status = this.verification()?.status;
    return this.auth.has(Perm.verificationCreate)
      && status !== 'Completed' && status !== 'Cancelled';
  });

  ngOnInit(): void {
    this.id = Number(this.route.snapshot.paramMap.get('id'));
    this.load();

    this.realtime.subscribeToVerification(this.id);
    syncOn([this.realtime.verificationChanged], () => this.load(), this.destroyRef);
  }

  ngOnDestroy(): void {
    this.realtime.unsubscribeFromVerification(this.id);
  }

  load(): void {
    this.api.verification(this.id).subscribe({
      next: (v) => { this.verification.set(v); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  claim(): void {
    this.api.claimVerification(this.id).subscribe(() => {
      this.toast.success('This is yours now.');
      this.load();
    });
  }

  assign(): void {
    const v = this.verification();
    if (!v) return;

    this.dialog
      .open(VerificationAssignDialog, {
        data: {
          verificationId: v.id,
          verificationNumber: v.verificationNumber,
          currentCheckerId: v.assignedToUserId ?? null,
          currentCheckerName: v.assignedToDisplayName ?? null,
        },
      })
      .afterClosed()
      .subscribe((updated?: unknown) => { if (updated) this.load(); });
  }

  start(): void {
    this.api.startVerification(this.id).subscribe(() => {
      this.toast.success('You are now checking this.');
      this.load();
    });
  }

  report(): void {
    // Recording a result is append-only in effect — the verdict and its findings cannot be
    // rewritten afterwards — so it earns a confirmation. The submit happens inside the dialog, so a
    // refusal leaves the checker looking at their own text and the server's message together.
    const data: ConfirmData = {
      title: verificationResultLabel(this.result),
      message: this.confirmMessage(),
      confirmText: 'Record it',
      submit: (context) => this.api.recordVerificationResult(
        this.id, { result: this.result, findings: this.findings.trim() }, context),
    };

    this.dialog.open(ConfirmDialog, { data }).afterClosed().subscribe((done) => {
      if (!done) return;
      this.findings = '';
      this.toast.success('Recorded. Whoever raised it has been told.');
      this.load();
    });
  }

  cancel(): void {
    // A reason dialog, not a confirmation: the server requires a reason and every call-off
    // otherwise carried the same canned sentence, which is no record at all.
    const data: ReasonData = {
      title: 'Call this check off?',
      message: 'It stays on the record with your reason. Any request waiting on it goes back to review.',
      label: 'Why is it being called off?',
      confirmText: 'Call it off',
      danger: true,
      submit: (reason, context) => this.api.cancelVerification(this.id, { reason }, context),
    };

    this.dialog.open(ReasonDialog, { data }).afterClosed().subscribe((done) => {
      if (done) this.load();
    });
  }

  targetLabel = (v: VerificationDetailDto) => verificationTargetLabel(v.targetType);

  /**
   * The sentence under the picker. Every one of these says the same thing in different words —
   * nothing is scheduled by recording an outcome — because that is the misunderstanding the whole
   * design is guarding against.
   */
  resultNote(): string {
    switch (this.result) {
      case 'IssueConfirmed':
        return 'This creates no task. The request goes back to whoever reviews it, with your '
          + 'findings attached, and approving it stays their decision.';
      case 'WorkingCorrectly':
        return 'The request goes back to review so somebody can close it down with your findings '
          + 'in front of them.';
      case 'ConfigurationOrDataIssue':
        return 'Real, but not software. It goes back to review, where it can be routed properly.';
      case 'NeedsClarification':
        return 'It goes back to review so the reviewer can go back to the person who raised it.';
      default:
        return 'An honest answer, and a common one. It goes back to review either way.';
    }
  }

  private confirmMessage(): string {
    const v = this.verification();
    const tail = v?.requestId
      ? ` ${v.requestNumber} goes back to review; nothing is scheduled by this.`
      : '';
    return `This is recorded against ${v?.verificationNumber} and cannot be rewritten afterwards.${tail}`;
  }
}
