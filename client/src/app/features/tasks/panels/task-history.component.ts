import { Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { AuthService } from '../../../core/auth.service';
import { Perm } from '../../../core/permissions';
import { TaskDetailDto, WorkTaskStatus } from '../../../core/models';
import { taskStatusLabel } from '../../../core/labels';
import { EmptyComponent } from '../../../shared/ui';

interface Entry {
  at: string;
  icon: string;
  text: string;
  who?: string | null;
  detail?: string | null;
  flagged?: boolean;
}

/**
 * Two accounts of the same task, kept apart.
 *
 * **What happened** is the `TaskActivity` stream: sentences somebody wrote for a reader, in the
 * order they occurred. It is what a requester, a worker or a manager wants when they ask "what has
 * been going on with this?".
 *
 * **Technical detail** is the state machine's own record — every transition as `from → to`, the
 * override flag, the reassignment trail. It answers a different question ("why is it in this state
 * and who put it there?") and it is only useful if you know what the states are.
 *
 * They used to be one list, built by rebuilding sentences out of the technical rows. That produced
 * a timeline where the interesting events were buried among enum names, and it duplicated wording
 * the server had already written. Now the readable stream is shown as written, the technical trail
 * is one toggle away, and the *audit* log — the administrator's before-and-after, with IP
 * addresses and entity names — stays where it was, on its own screen behind its own permission.
 * Three records, three audiences, no merging.
 */
@Component({
  selector: 'app-task-history',
  standalone: true,
  imports: [DatePipe, MatIconModule, MatTooltipModule, MatButtonToggleModule, EmptyComponent],
  template: `
    <div class="card card-pad">
      @if (canSeeTechnical()) {
        <div class="switcher">
          <mat-button-toggle-group [value]="view()" (change)="view.set($event.value)" hideSingleSelectionIndicator>
            <mat-button-toggle value="story">What happened</mat-button-toggle>
            <mat-button-toggle value="technical">Technical detail</mat-button-toggle>
          </mat-button-toggle-group>
        </div>
      }

      @if (entries().length === 0) {
        <app-empty
          [message]="view() === 'story' ? 'Nothing has happened yet' : 'No transitions recorded'"
          [hint]="view() === 'story'
            ? 'Starting work, pausing, finishing and quality checks all show up here.'
            : 'The status trail begins the first time this task moves.'"
          icon="history" />
      } @else {
        <div class="timeline">
          @for (entry of entries(); track $index) {
            <div class="entry" [class.flagged]="entry.flagged">
              <div class="marker"><mat-icon>{{ entry.icon }}</mat-icon></div>
              <div class="body">
                <div class="text">
                  {{ entry.text }}
                  @if (entry.flagged) {
                    <span class="chip tone-danger" matTooltip="Forced outside the normal workflow">
                      Override
                    </span>
                  }
                </div>
                @if (entry.detail) { <div class="muted small detail">{{ entry.detail }}</div> }
                <div class="muted small">
                  {{ entry.at | date: 'MMM d, y · HH:mm' }}@if (entry.who) { · {{ entry.who }} }
                </div>
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: `
    .switcher { display: flex; justify-content: flex-end; margin-bottom: 14px; }
    .timeline { display: flex; flex-direction: column; }
    .entry { display: flex; gap: 13px; padding-bottom: 16px; position: relative; }
    .entry:not(:last-child)::before {
      content: ''; position: absolute; left: 13px; top: 28px; bottom: 0;
      width: 2px; background: var(--border);
    }
    .marker {
      flex: 0 0 auto; width: 28px; height: 28px; border-radius: 50%;
      display: grid; place-items: center;
      background: var(--surface-sunken); border: 1px solid var(--border); z-index: 1;
    }
    .marker mat-icon { font-size: 15px; width: 15px; height: 15px; color: var(--text-muted); }
    .entry.flagged .marker { background: var(--tone-danger-bg); border-color: var(--tone-danger-fg); }
    .entry.flagged .marker mat-icon { color: var(--tone-danger-fg); }
    .body { min-width: 0; }
    .text { font-size: 13.5px; display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
    .detail { margin-top: 1px; white-space: pre-wrap; }
  `,
})
export class TaskHistoryComponent {
  private readonly auth = inject(AuthService);

  readonly task = input.required<TaskDetailDto>();
  readonly view = signal<'story' | 'technical'>('story');

  /**
   * The technical trail is for the people who run the process. Everyone else gets the account,
   * and no toggle to a view whose vocabulary is the schema. Not a security boundary — the server
   * decides what it sends, and a requester is sent neither stream.
   */
  readonly canSeeTechnical = computed(() =>
    this.auth.hasAny(Perm.taskAssign, Perm.taskReview, Perm.taskQCReview, Perm.adminViewAudit));

  readonly entries = computed<Entry[]>(() =>
    this.view() === 'technical' && this.canSeeTechnical() ? this.technical() : this.story());

  /** The readable stream, exactly as the server wrote it. Newest first. */
  private story(): Entry[] {
    return this.task().activity
      .map((a) => ({
        at: a.occurredAt,
        icon: this.iconForActivity(a.type),
        text: a.description,
        who: a.actorDisplayName,
      }))
      .sort((a, b) => new Date(b.at).getTime() - new Date(a.at).getTime());
  }

  /** Every transition and reassignment, in the state machine's own words. Newest first. */
  private technical(): Entry[] {
    const t = this.task();

    const statuses: Entry[] = t.statusHistory.map((h) => ({
      at: h.changedAt,
      icon: this.iconForStatus(h.toStatus),
      text: `${taskStatusLabel(h.fromStatus)} → ${taskStatusLabel(h.toStatus)}`,
      who: h.changedByDisplayName,
      detail: h.reason,
      flagged: h.wasOverride,
    }));

    const assignments: Entry[] = t.assignmentHistory.map((a) => ({
      at: a.assignedAt,
      icon: 'person',
      // Named on both sides: "Assignment changed" told the reader nothing they could act on.
      text: a.toDisplayName
        ? a.fromDisplayName
          ? `Moved from ${a.fromDisplayName} to ${a.toDisplayName}`
          : `Given to ${a.toDisplayName}`
        : 'Taken off everyone',
      who: a.assignedByDisplayName,
      detail: a.reason,
    }));

    return [...statuses, ...assignments]
      .sort((a, b) => new Date(b.at).getTime() - new Date(a.at).getTime());
  }

  private iconForActivity(type: string): string {
    switch (type) {
      case 'TaskStarted':
      case 'TaskResumed': return 'play_arrow';
      case 'TaskPaused': return 'pause';
      case 'TaskBlocked': return 'block';
      case 'TaskUnblocked': return 'lock_open';
      case 'TaskCompleted': return 'check';
      case 'TaskInterrupted': return 'swap_horiz';
      case 'QCStarted': return 'search';
      case 'QCPassed': return 'verified';
      case 'QCFailed': return 'replay';
      case 'TaskClosed': return 'lock';
      case 'TaskReopened': return 'lock_open';
      case 'AssignmentChanged': return 'person';
      case 'CollaboratorAdded':
      case 'CollaboratorRemoved': return 'group';
      case 'CommentAdded': return 'chat_bubble';
      case 'DependencyAdded':
      case 'DependencyRemoved': return 'link';
      case 'SubtaskCreated': return 'account_tree';
      case 'ScopeChanged':
      case 'ScopeChangeApproved': return 'straighten';
      case 'PriorityChanged': return 'priority_high';
      case 'TaskCreated': return 'add_task';
      default: return 'radio_button_unchecked';
    }
  }

  private iconForStatus(status: WorkTaskStatus): string {
    switch (status) {
      case 'InProgress': return 'play_arrow';
      case 'Paused': return 'pause';
      case 'Blocked': return 'block';
      case 'CompletedReadyForQC': return 'check';
      case 'QCReview': return 'search';
      case 'QCPassed': return 'verified';
      case 'QCFailedRework': return 'replay';
      case 'Closed': return 'lock';
      case 'Reopened': return 'lock_open';
      case 'Cancelled': return 'cancel';
      default: return 'radio_button_unchecked';
    }
  }
}
