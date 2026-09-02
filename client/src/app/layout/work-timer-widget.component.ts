import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { WorkTimerService } from '../core/work-timer.service';

/**
 * The running clock, in the top bar, on every screen.
 *
 * This is the whole reason the timer becomes visible rather than merely live: a clock that only
 * exists on the task you are looking at is a clock you cannot see while you are looking at
 * anything else, and the failure it has to prevent — a session left running through lunch, through
 * a meeting, through the rest of the afternoon — happens precisely when the worker is elsewhere.
 * Same reasoning the quick-work widget was built on, and the top bar is free because that one is
 * parked.
 *
 * It shows and does not act. Clicking goes to the task, where Pause, Blocked and Complete already
 * live with their confirmations, their reason dialogs and the shift handling. A second set of stop
 * controls here would be a second implementation of stopping work — the same trap My Queue avoids
 * by routing Start through the task detail with `?start=1`.
 */
@Component({
  selector: 'app-work-timer-widget',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatTooltipModule],
  template: `
    @if (timer.active(); as a) {
      <button matButton="filled" class="running" (click)="open(a.taskId)"
              [matTooltip]="a.title + ' — ' + timer.totalHuman() + ' on this task in total'">
        <mat-icon>timer</mat-icon>
        <span class="mono number">{{ a.taskNumber }}</span>
        <span class="mono clock">{{ timer.clock() }}</span>
      </button>
    }
  `,
  styles: `
    .running { background: var(--tone-running-bg); color: var(--tone-running-fg); }
    .number { margin-left: 2px; }
    /* Tabular figures, or the whole control shifts left and right once a second. */
    .clock { margin-left: 8px; font-variant-numeric: tabular-nums; }
    /* The number is the droppable half: a clock with no clock on it says nothing. */
    @media (max-width: 700px) { .number { display: none; } }
  `,
})
export class WorkTimerWidgetComponent {
  readonly timer = inject(WorkTimerService);
  private readonly router = inject(Router);

  open(taskId: number): void {
    void this.router.navigate(['/tasks', taskId]);
  }
}
