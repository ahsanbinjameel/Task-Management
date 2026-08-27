import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ApiService } from '../../core/api.service';
import { FormSubmit } from '../../core/form-submit';
import { VerificationDetailDto } from '../../core/models';
import { SearchSelectComponent, SelectOption } from '../../shared/search-select.component';

export interface AssignVerificationData {
  verificationId: number;
  verificationNumber: string;
  /** Who holds it now, if anyone. Its presence is what turns this into a reassignment. */
  currentCheckerId?: number | null;
  currentCheckerName?: string | null;
}

/**
 * Giving a check to a checker, or moving it to a different one.
 *
 * A purpose-built dialog is its own confirmation: it names what is being moved, says what happens,
 * and labels its own button — so it does not stack a `ConfirmDialog` on top. It performs the call
 * itself, which is what stops a server refusal throwing away the reason somebody just typed.
 *
 * The reason field appears only when somebody already holds it. Handing out work nobody had needs
 * no explanation; taking it off a person does — the same rule the task side applies to reassignment.
 */
@Component({
  selector: 'app-verification-assign-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule,
    MatInputModule, SearchSelectComponent,
  ],
  template: `
    <h2 mat-dialog-title>{{ isMove ? 'Move this check' : 'Assign a checker' }}</h2>

    <mat-dialog-content>
      <p class="lead">
        @if (isMove) {
          {{ data.verificationNumber }} is with {{ data.currentCheckerName }}. Moving it tells both
          people, and the reason goes on the record.
        } @else {
          {{ data.verificationNumber }} has nobody on it. Whoever you pick is told straight away.
        }
      </p>

      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <app-search-select
        class="full"
        label="Checker"
        name="assignToUserId"
        placeholder="Type to find someone"
        [options]="checkerOptions()"
        [(ngModel)]="assignToUserId" />

      @if (isMove) {
        <mat-form-field class="full">
          <mat-label>Why is it moving?</mat-label>
          <textarea matInput rows="3" name="reason" [(ngModel)]="reason" maxlength="2000"
                    placeholder="Quentin is on leave."
                    (input)="form.clearField('reason')"></textarea>
          @if (form.fieldError('reason'); as e) { <mat-error>{{ e }}</mat-error> }
        </mat-form-field>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [disabled]="!canSubmit() || form.busy()" (click)="save()">
        {{ form.busy() ? 'Saving…' : (isMove ? 'Move it' : 'Assign it') }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content { min-width: min(460px, 84vw); padding-top: 8px !important; }
    .full { width: 100%; }
    .lead { margin: 0 0 14px; color: var(--text-muted); font-size: 13px; line-height: 1.5; }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class VerificationAssignDialog implements OnInit {
  readonly data = inject<AssignVerificationData>(MAT_DIALOG_DATA);
  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<VerificationAssignDialog>);

  readonly form = new FormSubmit();
  readonly checkerOptions = signal<SelectOption[]>([]);

  assignToUserId: number | null = null;
  reason = '';

  readonly isMove = this.data.currentCheckerId != null;

  canSubmit(): boolean {
    if (this.assignToUserId === null) return false;
    if (this.assignToUserId === this.data.currentCheckerId) return false;
    return !this.isMove || this.reason.trim().length > 0;
  }

  ngOnInit(): void {
    this.api.assignableCheckers().subscribe((checkers) => {
      this.checkerOptions.set(
        checkers
          // The person who already has it is not a move.
          .filter((c) => c.userId !== this.data.currentCheckerId)
          .map((c) => ({
            value: c.userId,
            label: c.displayName,
            // The server sorts lightest-load first; saying how much each holds is what makes that
            // ordering usable rather than mysterious.
            hint: c.openVerifications === 0 ? 'free' : `${c.openVerifications} open`,
          })),
      );
    });
  }

  save(): void {
    this.form.run(
      (context) => this.api.assignVerification(
        this.data.verificationId,
        { assignToUserId: this.assignToUserId!, reason: this.reason.trim() || null },
        context),
      (updated: VerificationDetailDto) => this.ref.close(updated),
    );
  }
}
