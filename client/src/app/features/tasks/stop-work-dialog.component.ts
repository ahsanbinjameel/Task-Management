import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../core/api.service';
import { FormSubmit } from '../../core/form-submit';
import { PauseReasonDto, TaskDetailDto } from '../../core/models';
import { SearchSelectComponent } from '../../shared/search-select.component';

/** The dialog performs the pause/block itself, so it resolves with the updated task. */
export type StopWorkResult = TaskDetailDto;

export interface StopWorkData {
  mode: 'pause' | 'block';
  taskId: number;
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
    MatIconModule, SearchSelectComponent,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.mode === 'pause' ? 'Pause this work' : 'Cannot continue' }}</h2>

    <mat-dialog-content>
      <p class="muted small">
        {{ data.mode === 'pause'
            ? 'Your time so far is saved. You can start again whenever you are ready.'
            : 'Use this when something is stopping you from carrying on. It is different from taking a break.' }}
      </p>

      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <app-search-select class="full" label="Why are you stopping?" name="pausereasonid"
                         [options]="reasonOptions()" [ngModel]="pauseReasonId"
                         (ngModelChange)="pickReason($event)" />

      @if (chosen(); as r) {
        <p class="effect small">
          <mat-icon>{{ r.isBlocker ? 'report_problem' : 'schedule' }}</mat-icon>
          <span>
            {{ r.isBlocker
                ? 'This task will be marked as cannot continue, so someone can help get it moving.'
                : 'This task stays yours and waits for you.' }}
            @if (r.awayState) {
              You will be shown as {{ awayLabel(r.awayState) }}.
            } @else {
              You stay available for other work.
            }
          </span>
        </p>
      }

      <mat-form-field class="full">
        <mat-label>Details{{ commentRequired() ? '' : ' (optional)' }}</mat-label>
        <textarea matInput rows="3" name="comment" [(ngModel)]="comment"></textarea>
        @if (form.fieldError('comment'); as e) { <mat-error>{{ e }}</mat-error> }
      </mat-form-field>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [disabled]="!ready() || form.busy()" (click)="confirm()">
        {{ form.busy() ? 'Saving…' : (data.mode === 'pause' ? 'Pause' : 'Cannot continue') }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; }
    mat-dialog-content { min-width: min(440px, 82vw); }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
    /* Says what this choice will do -- to the work, and to you -- before it is made. */
    .effect {
      display: flex; align-items: flex-start; gap: 7px; margin: -4px 0 14px;
      color: var(--text-muted); line-height: 1.45;
    }
    .effect mat-icon { font-size: 17px; width: 17px; height: 17px; flex: none; margin-top: 1px; }
  `,
})
export class StopWorkDialog implements OnInit {
  private readonly api = inject(ApiService);
  readonly ref = inject(MatDialogRef<StopWorkDialog, StopWorkResult>);
  readonly data = inject<StopWorkData>(MAT_DIALOG_DATA);
  readonly form = new FormSubmit();

  readonly reasons = signal<PauseReasonDto[]>([]);
  readonly reasonOptions = computed(() => this.reasons().map((reason) => ({
    value: reason.id,
    label: reason.name,
    hint: reason.isBlocker ? 'cannot continue' : undefined,
  })));
  pauseReasonId: number | null = null;
  comment = '';

  /** The reason currently picked, if any. */
  readonly chosen = computed(() =>
    this.reasons().find((r) => r.id === this.selectedId()) ?? null);

  readonly commentRequired = computed(() => this.chosen()?.requiresComment ?? false);

  /** Mirrors `pauseReasonId` into a signal so `chosen` recomputes when the select changes. */
  private readonly selectedId = signal<number | null>(null);

  pickReason(id: number | null): void {
    this.pauseReasonId = id;
    this.selectedId.set(id);
  }

  awayLabel = (state: string): string =>
    state === 'Break' ? 'on a break'
    : state === 'Lunch' ? 'at lunch'
    : state === 'Meeting' ? 'in a meeting'
    : 'away';

  ngOnInit(): void {
    this.api.pauseReasons().subscribe((reasons) => {
      // Blocking reasons first when blocking, so the sensible choice is at the top.
      const relevant = this.data.mode === 'block'
        ? [...reasons].sort((a, b) => Number(b.isBlocker) - Number(a.isBlocker))
        : reasons;
      this.reasons.set(relevant);
    });
  }

  ready(): boolean {
    return !this.commentRequired() || this.comment.trim().length > 0;
  }

  confirm(): void {
    if (!this.ready()) return;

    const reasonId = this.pauseReasonId ?? undefined;
    const comment = this.comment.trim() || undefined;

    this.ref.disableClose = true;
    this.form.run(
      (ctx) => this.data.mode === 'pause'
        ? this.api.pauseWork(this.data.taskId, reasonId, comment, ctx)
        : this.api.blockWork(this.data.taskId, reasonId, comment, ctx),
      (task) => { this.ref.disableClose = false; this.ref.close(task); },
    );
  }
}
