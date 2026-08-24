import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../core/api.service';
import { FormSubmit } from '../../core/form-submit';
import { AssignableUserDto, TaskDetailDto, TaskSummaryDto } from '../../core/models';
import { SearchSelectComponent, SelectOption } from '../../shared/search-select.component';

export interface AssignDialogData {
  task: TaskSummaryDto | TaskDetailDto;
  /** True when the task already has an assignee — the API then demands a reason. */
  isReassign: boolean;
  currentAssigneeId?: number | null;
  rowVersion?: string | null;
}

/** The dialog performs the assignment, so it resolves with the updated task. */
export type AssignDialogResult = TaskDetailDto;

@Component({
  selector: 'app-assign-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatIconModule, SearchSelectComponent,
  ],
  template: `
    <h2 mat-dialog-title>
      {{ data.isReassign ? 'Change who is responsible for' : 'Choose who will do' }} {{ number() }}
    </h2>

    <mat-dialog-content>
      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <app-search-select label="Responsible person" [options]="responsibleOptions()"
                         nullLabel="Nobody — put it back in the waiting list"
                         [ngModel]="assigneeUserId()" name="assignee"
                         (ngModelChange)="assigneeUserId.set($event)" />

      <p class="muted small who">
        The person responsible owns this task. It appears in their queue and counts as their work.
      </p>

      <app-search-select label="Support people (optional)" multiple [options]="supportOptions()"
                         [ngModel]="supportUserIds()" (ngModelChange)="supportUserIds.set($event)"
                         name="support" />

      <p class="muted small who">
        Support people help with the task. It does not go into their queue and does not count as
        their work — they are shown on the task and in reports as having helped.
      </p>

      @if (data.isReassign) {
        <mat-form-field class="full">
          <mat-label>Why are you changing this?</mat-label>
          <textarea matInput rows="3" name="reason" [(ngModel)]="reason" required></textarea>
          @if (form.fieldError('reason'); as e) { <mat-error>{{ e }}</mat-error> }
        </mat-form-field>
      }
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [disabled]="!ready() || form.busy()" (click)="confirm()">
        {{ form.busy() ? 'Saving…' : (data.isReassign ? 'Change person' : 'Assign') }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; }
    mat-dialog-content { min-width: min(460px, 82vw); padding-top: 8px !important; }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
    .who { margin: -8px 0 14px; line-height: 1.45; }
  `,
})
export class AssignDialogComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly ref = inject(MatDialogRef<AssignDialogComponent, AssignDialogResult>);
  readonly data = inject<AssignDialogData>(MAT_DIALOG_DATA);

  readonly users = signal<AssignableUserDto[]>([]);
  /** A signal, because the support list is derived from it — it must drop whoever now owns the task. */
  readonly assigneeUserId = signal<number | null>(null);
  reason = '';

  number = () => 'taskNumber' in this.data.task ? this.data.task.taskNumber : '';

  ngOnInit(): void {
    this.api.assignableUsers().subscribe((users) => this.users.set(users));
  }

  readonly form = new FormSubmit();

  readonly supportUserIds = signal<number[]>([]);

  private option = (user: AssignableUserDto): SelectOption => ({
    value: user.id,
    label: user.displayName,
    chip: user.workforceState,
    chipKind: 'workforce',
  });

  readonly responsibleOptions = computed(() =>
    this.users().map((user) => ({
      ...this.option(user),
      disabled: user.id === this.data.currentAssigneeId,
    })));

  /** Whoever is about to own it cannot also be listed as helping with it. */
  readonly supportOptions = computed(() =>
    this.users().filter((user) => user.id !== this.assigneeUserId()).map(this.option));

  ready(): boolean {
    if (this.data.isReassign && !this.reason.trim()) return false;
    return this.assigneeUserId() !== this.data.currentAssigneeId;
  }

  /**
   * The dialog performs the assignment itself and closes only once the server has accepted it.
   *
   * That matters more here than in most forms: assignment is guarded by a row version, so a
   * genuine concurrent-edit conflict is an expected outcome. Closing first would have discarded
   * the chosen person and the typed reason at exactly the moment the user needs to retry.
   */
  confirm(): void {
    if (!this.ready()) return;

    this.ref.disableClose = true;
    this.form.run(
      (ctx) => this.api.assign(
        this.data.task.id,
        this.assigneeUserId(),
        this.reason.trim() || undefined,
        this.data.rowVersion,
        ctx),
      (task) => {
        // Support people are added after the assignment succeeds: they are a separate
        // relationship, and adding them to a task whose assignment was rejected would leave
        // helpers attached to work nobody owns.
        const support = this.supportUserIds();
        if (support.length === 0) {
          this.ref.disableClose = false;
          this.ref.close(task);
          return;
        }

        let remaining = support.length;
        let latest = task;
        const done = () => {
          if (--remaining > 0) return;
          this.ref.disableClose = false;
          this.ref.close(latest);
        };

        for (const userId of support) {
          this.api.addCollaborator(this.data.task.id, userId).subscribe({
            next: (updated) => { latest = updated; done(); },
            error: done,
          });
        }
      },
    );
  }
}
