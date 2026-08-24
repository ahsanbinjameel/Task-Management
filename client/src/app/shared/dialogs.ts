import { Component, inject } from '@angular/core';
import { HttpContext } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Observable } from 'rxjs';
import { FormSubmit } from '../core/form-submit';

export interface ConfirmData {
  title: string;
  message: string;
  confirmText?: string;
  danger?: boolean;
  /**
   * Optional. When supplied the dialog performs the operation itself and only closes if it
   * succeeds, so a failure leaves the user looking at the dialog and the reason together.
   */
  submit?: (context: HttpContext) => Observable<unknown>;
}

@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <p>{{ data.message }}</p>
      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [color]="data.danger ? 'warn' : 'primary'"
              [disabled]="form.busy()" (click)="confirm()">
        {{ form.busy() ? 'Working…' : (data.confirmText ?? 'Confirm') }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 12px 0 0;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class ConfirmDialog {
  readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
  readonly ref = inject(MatDialogRef<ConfirmDialog>);
  readonly form = new FormSubmit();

  confirm(): void {
    if (!this.data.submit) {
      this.ref.close(true);
      return;
    }

    this.ref.disableClose = true;
    this.form.run(
      (ctx) => this.data.submit!(ctx),
      (value) => { this.ref.disableClose = false; this.ref.close(value ?? true); },
      { focusFirstInvalid: false },
    );
  }
}

export interface ReasonData {
  title: string;
  message?: string;
  label?: string;
  /** When true the dialog will not close without text — mirrors the API's reason-required rule. */
  required?: boolean;
  confirmText?: string;
  danger?: boolean;
  /**
   * Optional. When supplied the dialog performs the operation itself: it stays open on failure
   * with the typed reason intact and the server's message shown inline, and closes only on success.
   *
   * Without it the dialog falls back to returning the text for the caller to use — which loses the
   * text if the call then fails, so prefer passing this.
   */
  submit?: (reason: string, context: HttpContext) => Observable<unknown>;
}

/**
 * Collects the mandatory reason behind reject, pause, block, QC fail, reopen, override and
 * reassign. The server rejects those calls without one, so asking here turns a 400 into a prompt.
 *
 * When `submit` is supplied the dialog also *performs* the action, which is what keeps a rejected
 * operation from throwing away what the user typed.
 */
@Component({
  selector: 'app-reason-dialog',
  standalone: true,
  imports: [
    MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatIconModule, FormsModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      @if (data.message) { <p class="muted">{{ data.message }}</p> }

      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <mat-form-field class="full">
        <mat-label>{{ data.label ?? 'Reason' }}</mat-label>
        <textarea matInput rows="4" name="reason" [(ngModel)]="reason" cdkFocusInitial
                  [required]="data.required !== false"></textarea>
        @if (form.fieldError('reason'); as e) { <mat-error>{{ e }}</mat-error> }
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [color]="data.danger ? 'warn' : 'primary'"
              [disabled]="!ready() || form.busy()"
              (click)="confirm()">
        {{ form.busy() ? 'Saving…' : (data.confirmText ?? 'Confirm') }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; margin-top: 8px; }
    mat-dialog-content { min-width: min(440px, 80vw); }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 8px 0 4px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class ReasonDialog {
  readonly data = inject<ReasonData>(MAT_DIALOG_DATA);
  readonly ref = inject(MatDialogRef<ReasonDialog>);
  readonly form = new FormSubmit();

  reason = '';

  ready = (): boolean => this.data.required === false || this.reason.trim().length > 0;

  confirm(): void {
    const reason = this.reason.trim();

    if (!this.data.submit) {
      this.ref.close(reason);
      return;
    }

    this.ref.disableClose = true;
    this.form.run(
      (ctx) => this.data.submit!(reason, ctx),
      (value) => { this.ref.disableClose = false; this.ref.close(value ?? reason); },
    );
  }
}
