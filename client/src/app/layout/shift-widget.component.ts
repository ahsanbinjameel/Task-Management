import { DestroyRef, Component, OnInit, computed, inject, signal } from '@angular/core';
import { syncOn } from '../core/realtime-sync';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDividerModule } from '@angular/material/divider';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { RealtimeService, WorkforceChangedEvent } from '../core/realtime.service';
import { ToastService } from '../core/toast.service';
import { WorkforceState, WorkforceStatusDto } from '../core/models';
import { humanizeEnum, workforceTone } from '../core/format';

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
          <button matButton="filled" (click)="startShift()" [disabled]="busy()">
            <mat-icon>play_circle</mat-icon> Start shift
          </button>
        } @else {
          <button matButton [matMenuTriggerFor]="shiftMenu" [disabled]="busy()" class="state-button">
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
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly realtime = inject(RealtimeService);
  private readonly toast = inject(ToastService);

  readonly status = signal<WorkforceStatusDto | null>(null);
  readonly busy = signal(false);

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
    this.run(this.api.startShift(), 'Shift started.');
  }

  endShift(): void {
    this.run(this.api.endShift(), 'Shift ended.');
  }

  setState(state: WorkforceState): void {
    this.run(this.api.setWorkforceState(state), `You are now ${humanizeEnum(state).toLowerCase()}.`);
  }

  label = (state: WorkforceState) => humanizeEnum(state);

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

  private run(call: import('rxjs').Observable<WorkforceStatusDto>, message: string): void {
    this.busy.set(true);
    call.subscribe({
      next: (s) => {
        this.status.set(s);
        this.busy.set(false);
        this.toast.success(message);
      },
      error: () => this.busy.set(false),
    });
  }
}
