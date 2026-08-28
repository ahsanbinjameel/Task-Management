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
      {{ data.isReassign ? 'Reassign' : 'Assign' }} {{ number() }}
    </h2>

    <mat-dialog-content>
      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <!--
        Two columns, because the list was the problem. Each candidate used to be a card carrying
        every fact about them, so eight people ran well past the bottom of a laptop screen and
        choosing one meant scrolling through the details of seven others.

        Now the list is dense enough to see at once and the facts about the one being considered
        use the width beside it. Still no capacity number: estimates are guesses, most tasks carry
        none, and a sum of guesses is not something anybody can act on.
      -->
      <div class="split">
        <section class="picker">
          @if (candidates().length > 6) {
            <input class="search" type="text" placeholder="Search" [value]="search()"
                   (input)="search.set($any($event.target).value)" aria-label="Search people" />
          }

          <div class="people" role="radiogroup" aria-label="Who will do this">
            @for (person of visible(); track person.userId) {
              <button type="button" class="person"
                      [class.chosen]="assigneeUserId() === person.userId"
                      [class.current]="person.userId === data.currentAssigneeId"
                      [disabled]="person.userId === data.currentAssigneeId"
                      [attr.aria-pressed]="assigneeUserId() === person.userId"
                      (click)="assigneeUserId.set(person.userId)">
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
                      <span class="now">On {{ person.activeTaskNumber }}</span>
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
                </span>
              </button>
            } @empty {
              <p class="muted small pad">
                @if (candidates().length === 0) {
                  Nobody holds the permission to do work yet.
                } @else {
                  Nobody matches "{{ search() }}".
                }
              </p>
            }

            <button type="button" class="person nobody" [class.chosen]="assigneeUserId() === null"
                    [disabled]="data.currentAssigneeId == null"
                    (click)="assigneeUserId.set(null)">
              <span class="dot"></span>
              <span class="detail"><span class="name">Nobody — back to the waiting list</span></span>
            </button>
          </div>
        </section>

        <aside class="chosen-panel">
          @if (selected(); as person) {
            <div class="who-head">
              <span class="dot" [class.on]="person.isOnShift"></span>
              <strong>{{ person.displayName }}</strong>
            </div>

            <dl class="stats">
              <div><dt>Shift</dt><dd>{{ person.isOnShift ? 'On shift' : 'Not on shift' }}</dd></div>
              @if (person.activeTaskNumber) {
                <div>
                  <dt>Working on</dt>
                  <dd>
                    {{ person.activeTaskNumber }}
                    @if (person.activeFor) { <span class="muted">({{ person.activeFor | duration }})</span> }
                  </dd>
                </div>
              }
              <div><dt>Active</dt><dd>{{ person.activeCount }}</dd></div>
              <div><dt>Waiting</dt><dd>{{ person.waitingCount }}</dd></div>
              <div><dt>Due today</dt><dd>{{ person.dueTodayCount }}</dd></div>
            </dl>

            @if (person.recentRelated.length) {
              <div class="related-block">
                <span class="k">Recent related work</span>
                <ul>
                  @for (title of person.recentRelated; track title) { <li>{{ title }}</li> }
                </ul>
              </div>
            }
          } @else if (assigneeUserId() === null && data.currentAssigneeId != null) {
            <p class="muted small">This goes back to the waiting list for somebody else to pick up.</p>
          } @else {
            <p class="muted small">Pick somebody to see what they are carrying.</p>
          }
        </aside>
      </div>

      <!-- Collapsed by default: helping is the exception, and it cost a block of prose every time. -->
      @if (showSupport()) {
        <app-search-select label="Support people" multiple [options]="supportOptions()"
                           [ngModel]="supportUserIds()" (ngModelChange)="supportUserIds.set($event)"
                           name="support" />
      } @else {
        <button type="button" class="link" (click)="showSupport.set(true)">
          <mat-icon>group_add</mat-icon> Add support people
        </button>
      }

      @if (data.isReassign) {
        <mat-form-field class="full">
          <mat-label>Why are you changing this?</mat-label>
          <textarea matInput rows="2" name="reason" [(ngModel)]="reason" required></textarea>
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
    mat-dialog-content { min-width: min(780px, 88vw); padding-top: 8px !important; }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }

    .split { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 260px); gap: 14px; }
    @media (max-width: 720px) { .split { grid-template-columns: 1fr; } }

    /* The list scrolls rather than the dialog: a long team should not make the buttons unreachable. */
    .people { max-height: 264px; overflow-y: auto; padding-right: 2px; }
    .search {
      width: 100%; margin-bottom: 6px; padding: 6px 9px; font: inherit; font-size: 13px;
      border: 1px solid var(--border); border-radius: 7px;
      background: var(--surface); color: var(--text);
    }

    .person {
      display: flex; align-items: flex-start; gap: 9px; width: 100%; text-align: left;
      padding: 7px 9px; margin-bottom: 3px; cursor: pointer;
      border: 1px solid transparent; border-radius: 7px; background: transparent;
      color: var(--text); font: inherit;
    }
    .person:hover:not(:disabled) { background: var(--surface-sunken); }
    .person.chosen { border-color: #1d69d4; background: var(--tone-running-bg); }
    .person:disabled { opacity: .55; cursor: default; }
    .person.nobody { margin-top: 4px; border-top: 1px solid var(--border); border-radius: 0 0 7px 7px; }

    /* Filled means on the clock: the one fact that decides whether the rest of the row matters. */
    .dot {
      width: 8px; height: 8px; border-radius: 50%; margin-top: 5px; flex: none;
      border: 1.5px solid var(--border-strong); background: transparent;
    }
    .dot.on { background: var(--tone-good-fg); border-color: var(--tone-good-fg); }

    .detail { display: flex; flex-direction: column; gap: 1px; min-width: 0; }
    .name { font-weight: 500; font-size: 13.5px; }
    .facts { display: flex; flex-wrap: wrap; gap: 4px; }
    .facts .now { color: var(--tone-running-fg); font-weight: 500; }
    .facts .due { color: var(--tone-warn-fg); }
    .pad { padding: 8px 9px; }

    .chosen-panel {
      border: 1px solid var(--border); border-radius: 8px; padding: 11px 12px;
      background: var(--surface-sunken); min-width: 0;
    }
    .who-head { display: flex; align-items: center; gap: 8px; margin-bottom: 9px; }
    .stats { margin: 0; display: flex; flex-direction: column; gap: 5px; }
    .stats > div { display: flex; justify-content: space-between; gap: 10px; font-size: 12.5px; }
    .stats dt { color: var(--text-muted); margin: 0; }
    .stats dd { margin: 0; font-weight: 500; text-align: right; min-width: 0; }
    .related-block { margin-top: 11px; padding-top: 9px; border-top: 1px solid var(--border); }
    .related-block .k { font-size: 12px; color: var(--text-muted); }
    .related-block ul { margin: 4px 0 0; padding-left: 16px; font-size: 12.5px; }
    .related-block li { margin-bottom: 2px; }

    .link {
      display: inline-flex; align-items: center; gap: 5px; margin-top: 12px;
      padding: 0; border: none; background: none; cursor: pointer;
      color: var(--tone-running-fg); font: inherit; font-size: 13px;
    }
    .link mat-icon { font-size: 17px; width: 17px; height: 17px; }
    app-search-select { display: block; margin-top: 12px; }
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
  readonly showSupport = signal(false);

  /** Only offered once the list is long enough that reading it stops being quicker than typing. */
  readonly search = signal('');

  readonly visible = computed(() => {
    const term = this.search().trim().toLowerCase();
    const people = this.candidates();
    return term ? people.filter((p) => p.displayName.toLowerCase().includes(term)) : people;
  });

  /** The one being considered, whose facts fill the panel beside the list. */
  readonly selected = computed(() =>
    this.candidates().find((p) => p.userId === this.assigneeUserId()) ?? null);

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
