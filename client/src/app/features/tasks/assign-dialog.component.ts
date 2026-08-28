import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../core/api.service';
import { FormSubmit } from '../../core/form-submit';
import {
  AssignableUserDto, AssignmentCandidateDto, TaskDetailDto, TaskSummaryDto,
} from '../../core/models';
import { DurationPipe } from '../../core/format';
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
    MatIconModule, SearchSelectComponent, DurationPipe,
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

      <!--
        Facts, not a fabricated capacity number (PRODUCT-CORE §12C). The old panel summed estimated
        hours and called it capacity: estimates are guesses, most tasks carry none, and adding
        guesses together does not make a fact. What a coordinator actually asks is who is here,
        what they are on this minute, what is already queued behind it, and whether they have seen
        this part of the product before.

        A list rather than a dropdown, because the facts are the decision — a dropdown would hide
        exactly what there is to read. Everyone who may hold work appears, including people with
        nothing on, since "who is free" is most of the question.
      -->
      <fieldset class="people">
        <legend class="muted small">Who will do this?</legend>

        @for (person of candidates(); track person.userId) {
          <label class="person" [class.chosen]="assigneeUserId() === person.userId"
                 [class.current]="person.userId === data.currentAssigneeId">
            <input type="radio" name="assignee"
                   [checked]="assigneeUserId() === person.userId"
                   [disabled]="person.userId === data.currentAssigneeId"
                   (change)="assigneeUserId.set(person.userId)" />

            <span class="dot" [class.on]="person.isOnShift"></span>

            <span class="detail">
              <span class="name">
                {{ person.displayName }}
                @if (person.userId === data.currentAssigneeId) {
                  <span class="muted small">— has it now</span>
                }
              </span>

              <span class="facts small muted">
                @if (person.activeTaskNumber) {
                  <span class="now">
                    Working now · {{ person.activeTaskNumber }}
                    @if (person.activeFor) { ({{ person.activeFor | duration }}) }
                  </span>
                } @else if (!person.isOnShift) {
                  <span>Not on shift</span>
                } @else {
                  <span>Free</span>
                }
                <span>· {{ person.activeCount }} active</span>
                <span>· {{ person.waitingCount }} waiting</span>
                @if (person.dueTodayCount > 0) {
                  <span class="due">· {{ person.dueTodayCount }} due today</span>
                }
              </span>

              @if (person.recentRelated.length) {
                <span class="related small muted">
                  Recent related: {{ person.recentRelated.join(' · ') }}
                </span>
              }
            </span>
          </label>
        } @empty {
          <p class="muted small">
            Nobody holds the permission to do work yet. Grant a role with Task.Work in Settings.
          </p>
        }

        <label class="person nobody" [class.chosen]="assigneeUserId() === null">
          <input type="radio" name="assignee" [checked]="assigneeUserId() === null"
                 [disabled]="data.currentAssigneeId == null"
                 (change)="assigneeUserId.set(null)" />
          <span class="dot"></span>
          <span class="detail">
            <span class="name">Nobody — put it back in the waiting list</span>
          </span>
        </label>
      </fieldset>

      <p class="muted small who">
        The person responsible owns this task. It appears in their queue and counts as their work,
        and they are notified as soon as you confirm.
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

    .people { border: none; padding: 0; margin: 0 0 14px; min-width: 0; }
    .people legend { padding: 0 0 6px; }
    .person {
      display: flex; align-items: flex-start; gap: 10px;
      padding: 9px 10px; border: 1px solid var(--border); border-radius: 8px;
      margin-bottom: 6px; cursor: pointer;
    }
    .person:hover { background: var(--surface-sunken); }
    .person.chosen { border-color: #1d69d4; background: var(--tone-running-bg); }
    .person.current { opacity: .6; cursor: default; }
    .person input { margin-top: 3px; flex: none; }
    /* Filled means on the clock: the one fact that decides whether the rest of the row matters. */
    .dot {
      width: 8px; height: 8px; border-radius: 50%; margin-top: 6px; flex: none;
      border: 1.5px solid var(--border-strong); background: transparent;
    }
    .dot.on { background: var(--tone-good-fg); border-color: var(--tone-good-fg); }
    .detail { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .name { font-weight: 500; }
    .facts { display: flex; flex-wrap: wrap; gap: 4px; }
    .facts .now { color: var(--tone-running-fg); font-weight: 500; }
    .facts .due { color: var(--tone-warn-fg); }
    .related { font-style: italic; }
    .nobody { margin-top: 10px; }
  `,
})
export class AssignDialogComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly ref = inject(MatDialogRef<AssignDialogComponent, AssignDialogResult>);
  readonly data = inject<AssignDialogData>(MAT_DIALOG_DATA);

  readonly users = signal<AssignableUserDto[]>([]);
  readonly candidates = signal<AssignmentCandidateDto[]>([]);
  /** A signal, because the support list is derived from it — it must drop whoever now owns the task. */
  readonly assigneeUserId = signal<number | null>(null);
  reason = '';

  number = () => 'taskNumber' in this.data.task ? this.data.task.taskNumber : '';

  ngOnInit(): void {
    // Two calls, because they answer different questions. The candidate list carries the facts for
    // choosing who is responsible; the plain user list still fills the support picker, where load
    // and shift are beside the point — helping is not owning.
    this.api.assignableUsers().subscribe((users) => this.users.set(users));
    this.api.assignmentCandidates(this.data.task.id)
      .subscribe({ next: (people) => this.candidates.set(people), error: () => undefined });
  }

  readonly form = new FormSubmit();

  readonly supportUserIds = signal<number[]>([]);

  private option = (user: AssignableUserDto): SelectOption => ({
    value: user.id,
    label: user.displayName,
    chip: user.workforceState,
    chipKind: 'workforce',
  });

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
