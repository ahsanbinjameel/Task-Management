import { Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TaskDetailDto } from '../../../core/models';
import { HumanizePipe } from '../../../core/format';

interface Entry {
  at: string;
  icon: string;
  text: string;
  detail?: string | null;
  flagged?: boolean;
}

/**
 * The task's whole story in one column: status changes, assignments and the activity trail merged
 * and ordered. History is append-only server-side, so this is a complete record — including the
 * overrides, which are flagged rather than hidden.
 */
@Component({
  selector: 'app-task-history',
  standalone: true,
  imports: [DatePipe, MatIconModule, MatTooltipModule, HumanizePipe],
  template: `
    <div class="card card-pad">
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
              <div class="muted small">{{ entry.at | date: 'MMM d, y · HH:mm' }}</div>
            </div>
          </div>
        }
      </div>
    </div>
  `,
  styles: `
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
  readonly task = input.required<TaskDetailDto>();

  readonly entries = computed<Entry[]>(() => {
    const t = this.task();

    const statuses: Entry[] = t.statusHistory.map((h) => ({
      at: h.changedAt,
      icon: this.iconFor(h.toStatus),
      text: `${this.words(h.fromStatus)} → ${this.words(h.toStatus)}`,
      detail: h.reason,
      flagged: h.wasOverride,
    }));

    const assignments: Entry[] = t.assignmentHistory.map((a) => ({
      at: a.assignedAt,
      icon: 'person',
      text: a.toUserId ? 'Assignment changed' : 'Unassigned',
      detail: a.reason,
    }));

    return [...statuses, ...assignments]
      .sort((a, b) => new Date(b.at).getTime() - new Date(a.at).getTime());
  });

  private words(value: string): string {
    return value.replace(/([a-z])([A-Z])/g, '$1 $2');
  }

  private iconFor(status: string): string {
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
