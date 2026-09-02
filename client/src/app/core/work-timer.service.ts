import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { RealtimeService } from './realtime.service';
import { ActiveWorkDto, TaskDetailDto } from './models';
import { Perm } from './permissions';
import { humanizeDuration, parseTimeSpan } from './format';

/**
 * The worker's running clock — one for the whole application.
 *
 * The timer has always been core (PRODUCT-CORE §10: "Start / Pause / Complete — KEEP (core)"), but
 * it was invisible while it ran: the only evidence a session was open was a bolt icon on a queue
 * row and a "Time logged" field that, by construction, *excluded the session you were in*
 * (`TaskQueryService.TotalWorked` sums ended sessions only). So the one number a worker wanted —
 * how long have I been on this — was the one number no screen showed, and a timer left running
 * over lunch looked exactly like one that had been stopped.
 *
 * Three things follow from making it live, and all three are why this is a service rather than a
 * `setInterval` in a component:
 *
 * - **One interval, one truth.** The top bar, the task detail and My Queue all render the same
 *   clock. Three components each ticking their own would drift apart within a minute and would
 *   each need their own copy of the fetch, the realtime subscription and the teardown.
 * - **The tick stops when nothing is running.** The interval is started and cleared by an effect
 *   on `active`, so an idle tab is not waking Angular's change detection every second forever.
 * - **Local actions are adopted, not re-fetched.** Starting, pausing and completing all return the
 *   full task, so {@link adopt} reads the answer straight off it. Asking the server what we just
 *   told it costs a round trip during which the clock says the wrong thing.
 *
 * Nothing here patches state from a realtime *payload* — the standing rule. A `taskChanged` for
 * one of the caller's own tasks triggers a re-fetch, which is what keeps a second tab or a second
 * machine honest: the single-active-session rule means starting work anywhere stops the clock
 * everywhere.
 */
@Injectable({ providedIn: 'root' })
export class WorkTimerService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly realtime = inject(RealtimeService);

  /** What is running, or null. The whole public state; everything else is derived. */
  readonly active = signal<ActiveWorkDto | null>(null);

  /** Ticked locally. The server told us when it started; asking again every second is a request per second per tab. */
  private readonly now = signal(Date.now());
  private ticker?: ReturnType<typeof setInterval>;

  /** How long the current sitting has run. */
  readonly elapsedMs = computed(() => {
    const a = this.active();
    if (!a) return 0;
    return Math.max(0, this.now() - new Date(a.startedAt).getTime());
  });

  /** How long the *task* has taken: what it had banked, plus the sitting in progress. */
  readonly totalMs = computed(() => {
    const a = this.active();
    return a ? parseTimeSpan(a.previouslyLogged) + this.elapsedMs() : 0;
  });

  /**
   * The stopwatch: `H:MM:SS` past an hour, `M:SS` before it. Seconds are the point — a clock that
   * only moves once a minute reads as broken, which is the whole reason this exists.
   */
  readonly clock = computed(() => formatClock(this.elapsedMs()));

  /**
   * The **total**, in the app's ordinary duration wording rather than as a second stopwatch.
   *
   * `formatClock` is wrong here: "1:50" beside a heading that says "Time logged" reads as an hour
   * and fifty minutes, not one minute fifty, and the two clocks on a task screen already show
   * different things (this sitting vs. every sitting). Keeping the total in the same "2m" / "3h
   * 25m" form every other screen uses means the number does not change shape when the timer
   * starts — only its value keeps moving.
   */
  readonly totalHuman = computed(() => humanizeDuration(this.totalMs()));

  constructor() {
    // Follow the session. Signing out must drop the clock, or the next person to sign in on this
    // browser inherits somebody else's running task.
    effect(() => {
      if (this.auth.isAuthenticated() && this.auth.has(Perm.taskWork)) {
        this.refresh();
      } else {
        this.active.set(null);
      }
    });

    // Only tick while something is running.
    effect(() => {
      const running = this.active() !== null;

      if (running && !this.ticker) {
        this.now.set(Date.now());
        this.ticker = setInterval(() => this.now.set(Date.now()), 1000);
      } else if (!running && this.ticker) {
        clearInterval(this.ticker);
        this.ticker = undefined;
      }
    });

    // Someone else's device, or another tab. Thin payload, so re-fetch rather than patch.
    this.realtime.taskChanged.subscribe((e) => {
      if (e.assigneeUserId === this.auth.user()?.id) this.refresh();
    });
  }

  refresh(): void {
    this.api.activeSession().subscribe({
      next: (a) => this.active.set(a ?? null),
      // A clock is a convenience. Failing to load one must never surface as an error on a screen
      // the reader opened for something else.
      error: () => undefined,
    });
  }

  /**
   * Reads the clock off a task the caller just acted on.
   *
   * Start, pause, block and complete all return the whole `TaskDetailDto`, so the answer is
   * already in hand: a session of mine that is Active means the clock is on this task, and its
   * absence on the task we were showing means it has just been stopped. A task that is neither —
   * somebody else's work, opened to read it — leaves the clock alone.
   */
  adopt(task: TaskDetailDto): void {
    const me = this.auth.user()?.id;
    const running = task.workSessions?.find((s) => s.status === 'Active' && s.userId === me);

    // Untracked, and guarded against a no-op write. Callers drive this from an effect on their own
    // task signal, so a plain read here would make that effect depend on `active` — and a fresh
    // object written on every run would then re-render the widget and the queue row for nothing.
    const current = untracked(this.active);

    if (running) {
      if (current?.sessionId === running.id) return;

      this.active.set({
        sessionId: running.id,
        taskId: task.id,
        taskNumber: task.taskNumber,
        title: task.title,
        startedAt: running.sessionStart,
        previouslyLogged: task.totalWorkedTime,
      });
      return;
    }

    if (current?.taskId === task.id) this.active.set(null);
  }

  /** True when this task is the one the clock is on. */
  isRunning(taskId: number): boolean {
    return this.active()?.taskId === taskId;
  }
}

/** Shared so the widget, the task detail and the queue cannot format the same number differently. */
export function formatClock(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;
  const pad = (n: number) => String(n).padStart(2, '0');

  return hours > 0 ? `${hours}:${pad(minutes)}:${pad(seconds)}` : `${minutes}:${pad(seconds)}`;
}
