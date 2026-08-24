import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpContext } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { ToastService } from '../core/toast.service';
import { FormSubmit } from '../core/form-submit';
import { QuickWorkDto } from '../core/models';
import { humanizeDuration, parseTimeSpan } from '../core/format';

/**
 * Starting quick work. One field, because anything more and the person with a caller on hold will
 * not bother — and the time will be lost rather than recorded, which is the whole problem.
 */
@Component({
  selector: 'app-start-quick-work-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule,
  ],
  template: `
    <h2 mat-dialog-title>Record something you are doing now</h2>
    <mat-dialog-content>
      <p class="muted small lead">
        For work that never came through a request — a phone call, someone at your desk. Whatever
        task you have running is paused and handed back when you finish.
      </p>

      <mat-form-field appearance="outline" class="full">
        <mat-label>What is it?</mat-label>
        <input matInput name="title" [(ngModel)]="title" cdkFocusInitial maxlength="200"
               (keydown.enter)="submit()" />
      </mat-form-field>

      <mat-form-field appearance="outline" class="full">
        <mat-label>Who is it for? (leave blank for internal)</mat-label>
        <input matInput name="clientName" [(ngModel)]="clientName" maxlength="200" />
      </mat-form-field>

      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" (click)="submit()" [disabled]="!title.trim() || form.busy()">
        Start the clock
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content { min-width: min(440px, 82vw); }
    .lead { margin: 0 0 14px; }
    .full { width: 100%; }
    .form-error { display: flex; gap: 7px; align-items: flex-start; color: var(--tone-danger-fg); }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class StartQuickWorkDialog {
  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<StartQuickWorkDialog>);
  readonly form = new FormSubmit();

  title = '';
  clientName = '';

  submit(): void {
    if (!this.title.trim()) return;

    this.ref.disableClose = true;
    this.form.run(
      (ctx: HttpContext) => this.api.startQuickWork(
        { title: this.title.trim(), clientName: this.clientName.trim() || null }, ctx),
      (started) => { this.ref.disableClose = false; this.ref.close(started); },
    );
  }
}

/** Finishing it. The outcome is required — a record of forty busy minutes and nothing else is a
 * gap in the day dressed up as data. */
@Component({
  selector: 'app-finish-quick-work-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatCheckboxModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>
      <p class="muted small lead">Ran for {{ elapsed }}.</p>

      <mat-form-field appearance="outline" class="full">
        <mat-label>What came of it?</mat-label>
        <textarea matInput name="outcome" rows="3" [(ngModel)]="outcome" cdkFocusInitial
                  maxlength="2000"></textarea>
      </mat-form-field>

      @if (data.interruptedTaskNumber) {
        <mat-checkbox name="resume" [(ngModel)]="resume">
          Pick {{ data.interruptedTaskNumber }} back up
        </mat-checkbox>
      }

      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Keep going</button>
      <button matButton="filled" (click)="submit()" [disabled]="!outcome.trim() || form.busy()">
        Finish
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content { min-width: min(440px, 82vw); }
    .lead { margin: 0 0 14px; }
    .full { width: 100%; }
    .form-error { display: flex; gap: 7px; align-items: flex-start; color: var(--tone-danger-fg); margin-top: 10px; }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class FinishQuickWorkDialog {
  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<FinishQuickWorkDialog>);
  readonly data = inject<QuickWorkDto>(MAT_DIALOG_DATA);
  readonly form = new FormSubmit();

  outcome = '';
  resume = true;

  readonly elapsed = humanizeDuration(parseTimeSpan(this.data.duration));

  submit(): void {
    if (!this.outcome.trim()) return;

    this.ref.disableClose = true;
    this.form.run(
      (ctx: HttpContext) => this.api.finishQuickWork(
        this.data.id,
        { outcome: this.outcome.trim(), resumeInterruptedTask: this.resume },
        ctx),
      (finished) => { this.ref.disableClose = false; this.ref.close(finished); },
    );
  }
}

/**
 * The top-bar control for work that arrived without a request.
 *
 * It sits next to the shift widget and follows the same rule: it renders nothing for people whose
 * time is not tracked, because there would be nowhere for the record to land. While something is
 * running it shows a live clock, which is the point — an unnoticed timer left going over lunch is
 * worse than no timer at all.
 */
@Component({
  selector: 'app-quick-work-widget',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatTooltipModule],
  template: `
    @if (auth.tracksShift()) {
      @if (active(); as q) {
        <button matButton="filled" class="running" (click)="finish(q)"
                [matTooltip]="'Recording: ' + q.title + '. Click to finish.'">
          <mat-icon>bolt</mat-icon>
          <span class="truncate label">{{ q.title }}</span>
          <span class="mono clock">{{ elapsed() }}</span>
        </button>
      } @else {
        <button matButton (click)="start()" matTooltip="Record a phone call or a walk-up">
          <mat-icon>bolt</mat-icon> Quick work
        </button>
      }
    }
  `,
  styles: `
    .running { background: var(--tone-running-bg); color: var(--tone-running-fg); }
    .label { max-width: 160px; display: inline-block; vertical-align: bottom; }
    .clock { margin-left: 8px; font-variant-numeric: tabular-nums; }
    @media (max-width: 700px) { .label { display: none; } }
  `,
})
export class QuickWorkWidgetComponent implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  readonly auth = inject(AuthService);

  readonly active = signal<QuickWorkDto | null>(null);

  /**
   * Ticked locally rather than re-fetched. The server already told us when it started; asking it
   * again every second to be told the same thing would be a request per second per open tab.
   */
  readonly elapsed = signal('0m');
  private timer?: ReturnType<typeof setInterval>;

  ngOnInit(): void {
    if (!this.auth.tracksShift()) return;

    this.load();
    this.timer = setInterval(() => this.tick(), 1000);
  }

  ngOnDestroy(): void {
    if (this.timer) clearInterval(this.timer);
  }

  private load(): void {
    this.api.activeQuickWork().subscribe({
      next: (q) => { this.active.set(q); this.tick(); },
      error: () => undefined,
    });
  }

  private tick(): void {
    const q = this.active();
    if (!q) return;

    const ms = Date.now() - new Date(q.startedAt).getTime();
    const minutes = Math.floor(ms / 60000);
    const seconds = Math.floor((ms % 60000) / 1000);

    this.elapsed.set(minutes < 60
      ? `${minutes}:${String(seconds).padStart(2, '0')}`
      : `${Math.floor(minutes / 60)}h ${minutes % 60}m`);
  }

  start(): void {
    this.dialog.open(StartQuickWorkDialog).afterClosed().subscribe((started?: QuickWorkDto) => {
      if (!started) return;
      this.active.set(started);
      this.tick();
      this.toast.success('Recording. Finish it when you are done.');
    });
  }

  finish(quick: QuickWorkDto): void {
    this.dialog
      .open(FinishQuickWorkDialog, { data: quick })
      .afterClosed()
      .subscribe((finished?: QuickWorkDto) => {
        if (!finished) return;
        this.active.set(null);
        this.toast.success(
          finished.interruptedTaskNumber
            ? `Recorded. ${finished.interruptedTaskNumber} is running again.`
            : 'Recorded.');
      });
  }
}
