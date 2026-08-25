import { DestroyRef, Component, OnInit, computed, inject, signal } from '@angular/core';
import { syncOn } from '../core/realtime-sync';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDividerModule } from '@angular/material/divider';
import { ApiService } from '../core/api.service';
import { MatDialog } from '@angular/material/dialog';
import { HttpContext } from '@angular/common/http';
import { ConfirmDialog, ConfirmData } from '../shared/dialogs';
import { AuthService } from '../core/auth.service';
import { RealtimeService, WorkforceChangedEvent } from '../core/realtime.service';
import { ToastService } from '../core/toast.service';
import { WorkforceState, WorkforceStatusDto } from '../core/models';
import { workforceTone } from '../core/format';
import { workforceStateLabel } from '../core/labels';

/**
 * The shift and availability control in the top bar.
 *
 * It renders nothing at all for people who are not on the clock. The API tells us via
 * `isShiftTracked`, so a reviewer or coordinator never sees a Start Shift button that would 403 —
 * and the decision stays server-side where the permission lives, rather than being guessed here
 * from a role name.
 */
@Component({
  selector: 'app-shift-widget',
  standalone: true,
  imports: [MatIconModule, MatButtonModule, MatMenuModule, MatTooltipModule, MatDividerModule],
  template: `
    @if (status(); as s) {
      @if (s.isShiftTracked) {
        @if (!s.isOnShift) {
          <button matButton="filled" (click)="startShift()">
            <mat-icon>play_circle</mat-icon> Start shift
          </button>
        } @else {
          <button matButton [matMenuTriggerFor]="shiftMenu" class="state-button">
            <span class="chip dot" [class]="'tone-' + tone()">{{ s.stateLabel }}</span>
            <mat-icon iconPositionEnd>expand_more</mat-icon>
          </button>

          <mat-menu #shiftMenu="matMenu">
            <div class="menu-head">
              <strong>On shift</strong>
              <span class="muted small">since {{ shiftStarted() }}</span>
            </div>
            <mat-divider />

            @for (state of s.availableStates; track state) {
              <button mat-menu-item (click)="setState(state)" [disabled]="state === s.state">
                <mat-icon>{{ icon(state) }}</mat-icon>
                <span>{{ label(state) }}</span>
              </button>
            }

            <mat-divider />
            <button mat-menu-item (click)="endShift()">
              <mat-icon>stop_circle</mat-icon><span>End shift</span>
            </button>
          </mat-menu>
        }
      }
    }
  `,
  styles: `
    .state-button { padding-left: 8px; }
    .menu-head { display: flex; flex-direction: column; gap: 2px; padding: 10px 16px; }
  `,
})
export class ShiftWidgetComponent implements OnInit {
  private readonly dialog = inject(MatDialog);

  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly realtime = inject(RealtimeService);
  private readonly toast = inject(ToastService);

  readonly status = signal<WorkforceStatusDto | null>(null);

  readonly tone = computed(() => {
    const state = this.status()?.state;
    return state ? workforceTone(state) : 'neutral';
  });

  readonly shiftStarted = computed(() => {
    const start = this.status()?.currentShift?.shiftStart;
    return start ? new Date(start).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '';
  });

  ngOnInit(): void {
   this.load();
    // Re-fetch on the server's say-so; see syncOn for why it debounces and tears down.
    syncOn<WorkforceChangedEvent>(
      [this.realtime.workforceChanged],
      () => this.load(),
      this.destroyRef,
      { filter: (e) => e.userId === this.auth.user()?.id });
  }

  load(): void {
    this.api.myShiftStatus().subscribe({
      next: (s) => this.status.set(s),
      error: () => undefined,
    });
  }

  startShift(): void {
    // Starting the shift is the moment attendance begins being recorded, and the clock does not
    // rewind: a shift opened by accident at 08:00 and noticed at 09:00 shows an hour that was
    // never worked, and only a supervisor can close it early. Cheap to ask, awkward to correct.
    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: {
          title: 'Start your shift?',
          message:
            'Your hours are recorded from now until you end it. You will show as available to '
            + 'coordinators looking for someone to take work.',
          confirmText: 'Start my shift',
          submit: (ctx: HttpContext) => this.api.startShift(ctx),
        },
      })
      .afterClosed()
      .subscribe((started?: unknown) => {
        if (!started) return;
        this.toast.success('Shift started.');
        this.load();
      });
  }

  endShift(): void {
    // The server refuses this outright while a work session is open, so the dialog is not what
    // enforces it — it is what stops someone clicking through a menu and then reading a 409 they
    // did not expect. Ending a shift is also the one action here that cannot be undone: the
    // session is closed and the day's total is fixed.
    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: {
          title: 'Finish for the day?',
          message: this.status()?.state === 'Working'
            ? 'The timer is still running on a task. Pause or finish that work first, then end '
              + 'your shift.'
            : 'Your shift is closed and today\'s hours are final. Starting again opens a new one.',
          confirmText: 'End my shift',
          submit: (ctx: HttpContext) => this.api.endShift(ctx),
        },
      })
      .afterClosed()
      .subscribe((done?: unknown) => {
        if (!done) return;
        this.toast.success('Shift ended.');
        this.load();
      });
  }

  setState(state: WorkforceState): void {
    const label = workforceStateLabel(state).toLowerCase();

    // Availability is what the timeline is built from and what "who's working" shows, so a
    // mis-click writes a stretch of break into someone's day that they then have to explain. It is
    // undoable — switch back — but the interval that was recorded stays in the timeline either way.
    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: {
          title: `Change your status to ${label}?`,
          message: state === 'Available'
            ? 'You will show as free to take work, and the time from now counts as available.'
            : `The time from now is recorded as ${label} on your timeline and in your daily `
              + 'report, until you set your status back.',
          confirmText: `Set ${label}`,
          submit: (ctx: HttpContext) => this.api.setWorkforceState(state, undefined, ctx),
        },
      })
      .afterClosed()
      .subscribe((changed?: unknown) => {
        if (!changed) return;
        this.toast.success(`You are now ${label}.`);
        this.load();
      });
  }

  label = (state: WorkforceState) => workforceStateLabel(state);

  icon(state: WorkforceState): string {
    switch (state) {
      case 'Available': return 'check_circle';
      case 'Break': return 'coffee';
      case 'Lunch': return 'restaurant';
      case 'Meeting': return 'groups';
      case 'TemporarilyAway': return 'time_to_leave';
      case 'Working': return 'bolt';
      default: return 'radio_button_unchecked';
    }
  }
}
