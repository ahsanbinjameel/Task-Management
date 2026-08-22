import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export interface ConfirmData {
  title: string;
  message: string;
  confirmText?: string;
  danger?: boolean;
}

@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content><p>{{ data.message }}</p></mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>Cancel</button>
      <button matButton="filled" [color]="data.danger ? 'warn' : 'primary'" [mat-dialog-close]="true">
        {{ data.confirmText ?? 'Confirm' }}
      </button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDialog {
  readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
}

export interface ReasonData {
  title: string;
  message?: string;
  label?: string;
  /** When true the dialog will not close without text — mirrors the API's reason-required rule. */
  required?: boolean;
  confirmText?: string;
  danger?: boolean;
}

/**
 * Collects the mandatory reason behind reject, pause, block, QC fail, reopen, override and
 * reassign. The server rejects those calls without one, so asking here turns a 400 into a prompt.
 */
@Component({
  selector: 'app-reason-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, FormsModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      @if (data.message) { <p class="muted">{{ data.message }}</p> }
      <mat-form-field class="full">
        <mat-label>{{ data.label ?? 'Reason' }}</mat-label>
        <textarea matInput rows="4" [(ngModel)]="reason" cdkFocusInitial
                  [required]="data.required !== false"></textarea>
        @if (data.required !== false) {
          <mat-hint>Recorded on the task history and visible to everyone.</mat-hint>
        }
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>Cancel</button>
      <button matButton="filled" [color]="data.danger ? 'warn' : 'primary'"
              [disabled]="data.required !== false && !reason.trim()"
              (click)="ref.close(reason.trim())">
        {{ data.confirmText ?? 'Confirm' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; margin-top: 8px; }
    mat-dialog-content { min-width: min(440px, 80vw); }
  `,
})
export class ReasonDialog {
  readonly data = inject<ReasonData>(MAT_DIALOG_DATA);
  readonly ref = inject(MatDialogRef<ReasonDialog>);
  reason = '';
}
