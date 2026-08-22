import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ApiService } from '../../core/api.service';
import { PauseReasonDto } from '../../core/models';

export interface StopWorkResult {
  pauseReasonId?: number;
  comment?: string;
}

/**
 * Pause and Blocked share this dialog because they share the rule: some reasons demand a comment,
 * and the API says which. `requiresComment` comes from the configured reason, so the requirement is
 * enforced here as well as server-side and the user is told before they submit.
 */
@Component({
  selector: 'app-stop-work-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatSelectModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.mode === 'pause' ? 'Pause work' : 'Mark as blocked' }}</h2>

    <mat-dialog-content>
      <p class="muted small">
        {{ data.mode === 'pause'
            ? 'Your time so far is kept. Resume whenever you are ready.'
            : 'Use this when you cannot proceed — it is different from stepping away.' }}
      </p>

      <mat-form-field class="full">
        <mat-label>Reason</mat-label>
        <mat-select [(ngModel)]="pauseReasonId">
          @for (reason of reasons(); track reason.id) {
            <mat-option [value]="reason.id">{{ reason.name }}</mat-option>
          }
        </mat-select>
      </mat-form-field>

      <mat-form-field class="full">
        <mat-label>Comment{{ commentRequired() ? '' : ' (optional)' }}</mat-label>
        <textarea matInput rows="3" [(ngModel)]="comment"></textarea>
        @if (commentRequired()) {
          <mat-hint>This reason requires a comment.</mat-hint>
        }
      </mat-form-field>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>Cancel</button>
      <button matButton="filled" [disabled]="!valid()" (click)="confirm()">
        {{ data.mode === 'pause' ? 'Pause' : 'Mark blocked' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; }
    mat-dialog-content { min-width: min(440px, 82vw); }
  `,
})
export class StopWorkDialog implements OnInit {
  private readonly api = inject(ApiService);
  readonly ref = inject(MatDialogRef<StopWorkDialog, StopWorkResult>);
  readonly data = inject<{ mode: 'pause' | 'block' }>(MAT_DIALOG_DATA);

  readonly reasons = signal<PauseReasonDto[]>([]);
  pauseReasonId: number | null = null;
  comment = '';

  readonly commentRequired = computed(() =>
    this.reasons().find((r) => r.id === this.pauseReasonId)?.requiresComment ?? false);

  ngOnInit(): void {
    this.api.pauseReasons().subscribe((reasons) => {
      // Blocking reasons first when blocking, so the sensible choice is at the top.
      const relevant = this.data.mode === 'block'
        ? [...reasons].sort((a, b) => Number(b.isBlocker) - Number(a.isBlocker))
        : reasons;
      this.reasons.set(relevant);
    });
  }

  valid(): boolean {
    return !this.commentRequired() || this.comment.trim().length > 0;
  }

  confirm(): void {
    this.ref.close({
      pauseReasonId: this.pauseReasonId ?? undefined,
      comment: this.comment.trim() || undefined,
    });
  }
}
