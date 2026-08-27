import { DestroyRef, Component, OnInit, inject, signal } from '@angular/core';
import { RealtimeService } from '../../core/realtime.service';
import { syncOn } from '../../core/realtime-sync';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ApiService } from '../../core/api.service';
import { DailyTimelineDto, DailyUserReportDto } from '../../core/models';
import { DurationPipe, isoDate } from '../../core/format';
import { MatDialog } from '@angular/material/dialog';
import { openPdf } from '../../shared/pdf-viewer.component';
import {
  EmptyComponent, LoadingComponent, PageHeaderComponent, StatComponent,
} from '../../shared/ui';

/**
 * The user's own attendance day: the timeline built from the activity stream, and the effort split
 * by task. Same calculation the supervisory screens use, so the two can never disagree.
 */
@Component({
  selector: 'app-my-day',
  standalone: true,
  imports: [
    DatePipe, FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule,
    MatInputModule, PageHeaderComponent, StatComponent, EmptyComponent, LoadingComponent,
    DurationPipe,
  ],
  template: `
    <div class="page">
      <app-page-header title="My day">
        <mat-form-field class="date">
          <mat-label>Date</mat-label>
          <input matInput type="date" [(ngModel)]="date" (change)="load()" />
        </mat-form-field>
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon></button>
        <button matButton="filled" (click)="viewPdf()">
          <mat-icon>picture_as_pdf</mat-icon> View PDF
        </button>
      </app-page-header>

      @if (loading()) {
        <app-loading />
      } @else {
        @if (report(); as r) {
          <div class="stats">
            <app-stat label="On shift" [value]="(r.shiftDuration | duration)" />
            <app-stat label="Productive" [value]="(r.productiveTime | duration)" />
            <app-stat label="Away" [value]="(r.breakTime | duration)" />
            <app-stat label="Tasks worked" [value]="r.tasksWorked" />
            <app-stat label="Completed" [value]="r.tasksCompleted" />
            <app-stat label="Quick work" [value]="(r.quickWorkTime | duration)"
                      [hint]="r.interruptions === 1
                        ? '1 interruption'
                        : r.interruptions + ' interruptions'" />
          </div>
        }

        <div class="two-col top-gap">
          <div class="card">
            <div class="card-pad"><h2 class="card-title" style="margin:0">Timeline</h2></div>
            @if (!timeline() || timeline()!.entries.length === 0) {
              <app-empty message="Nothing recorded today" icon="schedule" />
            } @else {
              @for (entry of timeline()!.entries; track $index) {
                <div class="entry">
                  <span class="mono small time">
                    {{ entry.from | date: 'HH:mm' }}–{{ entry.isOpen ? 'now' : (entry.to | date: 'HH:mm') }}
                  </span>
                  <span class="label">{{ entry.label }}</span>
                  <span class="spacer"></span>
                  <span class="mono small">{{ entry.duration | duration }}</span>
                </div>
              }
            }
          </div>

          <div class="card">
            <div class="card-pad"><h2 class="card-title" style="margin:0">My tasks</h2></div>
            @if (!report() || report()!.ownedWork.length === 0) {
              <app-empty message="You have not logged time on your own tasks today" icon="timer_off"
                         actionLabel="Open my queue" actionRoute="/my-queue" />
            } @else {
              @for (line of report()!.ownedWork; track line.taskId) {
                <a class="entry" [routerLink]="['/tasks', line.taskId]">
                  <span class="mono small time">{{ line.taskNumber }}</span>
                  <span class="label truncate">{{ line.title }}</span>
                  <span class="spacer"></span>
                  <span class="muted small">{{ line.sessions }}×</span>
                  <span class="mono small">{{ line.timeSpent | duration }}</span>
                </a>
              }
            }
          </div>

          <!--
            Quick work gets a panel of its own rather than a line among the tasks. It is the answer
            to "where did the other two hours go", and burying it in a list of task numbers would
            be the same as not showing it.
          -->
          @if (report(); as r) {
            @if (r.quickWork.length > 0) {
              <div class="card">
                <div class="card-pad">
                  <h2 class="card-title" style="margin:0">Work that arrived without a request</h2>
                  <p class="muted small" style="margin:4px 0 0">
                    Phone calls and walk-ups. Recorded so the day adds up.
                  </p>
                </div>
                @for (q of r.quickWork; track q.id) {
                  <div class="entry" [class.cancelled]="q.wasCancelled">
                    <span class="mono small time">{{ q.startedAt | date: 'HH:mm' }}</span>
                    <div class="label grow">
                      <div class="truncate">{{ q.title }}</div>
                      @if (q.outcome) { <div class="muted small truncate">{{ q.outcome }}</div> }
                    </div>
                    <span class="spacer"></span>
                    @if (q.promotedToRequestNumber) {
                      <span class="chip tone-good nowrap">{{ q.promotedToRequestNumber }}</span>
                    }
                    @if (q.interruptedTaskNumber) {
                      <span class="muted small nowrap">interrupted {{ q.interruptedTaskNumber }}</span>
                    }
                    @if (q.wasCancelled) {
                      <span class="chip tone-muted">Not counted</span>
                    } @else {
                      <span class="mono small">{{ q.duration | duration }}</span>
                    }
                  </div>
                }
              </div>
            }

            @if (r.supportWork.length > 0 || r.supportingOn.length > 0) {
              <div class="card">
                <div class="card-pad">
                  <h2 class="card-title" style="margin:0">Tasks I helped with</h2>
                  <p class="muted small" style="margin:4px 0 0">
                    Somebody else is responsible for these. They do not count as your work.
                  </p>
                </div>
                @for (line of r.supportWork; track line.taskId) {
                  <a class="entry" [routerLink]="['/tasks', line.taskId]">
                    <span class="mono small time">{{ line.taskNumber }}</span>
                    <span class="label truncate">{{ line.title }}</span>
                    <span class="spacer"></span>
                    <span class="mono small">{{ line.timeSpent | duration }}</span>
                  </a>
                }
                @for (s of r.supportingOn; track s.taskId) {
                  <a class="entry" [routerLink]="['/tasks', s.taskId]">
                    <span class="mono small time">{{ s.taskNumber }}</span>
                    <span class="label truncate">{{ s.title }}</span>
                    <span class="spacer"></span>
                    <span class="muted small">
                      {{ s.responsiblePersonName ? s.responsiblePersonName + ' is responsible' : '' }}
                    </span>
                  </a>
                }
              </div>
            }
          }
        </div>
      }
    </div>
  `,
  styles: `
    .stats { display: grid; gap: 14px; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); }
    .two-col { display: grid; gap: 16px; grid-template-columns: repeat(auto-fit, minmax(340px, 1fr)); }
    .top-gap { margin-top: 18px; }
    .date { width: 170px; margin-bottom: -1.25em; }
    .entry .grow { flex: 1 1 auto; min-width: 0; }
    /* Struck through rather than hidden: a mis-click is history, it just does not count. */
    .entry.cancelled .label { text-decoration: line-through; color: var(--text-muted); }
    .entry {
      display: flex; align-items: center; flex-wrap: wrap; gap: 12px;
      padding: 9px 20px; border-top: 1px solid var(--border);
      color: inherit; text-decoration: none; font-size: 13.5px;
    }
    a.entry:hover { background: var(--surface-sunken); }
    .time { flex: 0 0 auto; color: var(--text-muted); min-width: 88px; }
    .label { min-width: 0; }
  `,
})
export class MyDayComponent implements OnInit {
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);

  readonly timeline = signal<DailyTimelineDto | null>(null);
  readonly report = signal<DailyUserReportDto | null>(null);
  readonly loading = signal(true);

  date = isoDate();

  ngOnInit(): void {
   this.load();
    // Re-fetch on the server's say-so; see syncOn for why it debounces and tears down.
    syncOn(
      [this.realtime.workforceChanged, this.realtime.taskChanged],
      () => this.load(),
      this.destroyRef);
  }

  /** The day as something to file or hand over — the same figures the page is showing. */
  /** Opens it to be read. Keeping a copy is a button inside the viewer. */
  viewPdf(): void {
    openPdf(this.dialog, {
      title: `My day — ${this.date}`,
      fileName: `my-day-${this.date}.pdf`,
      load: () => this.api.myDailyPdf(this.date),
    });
  }

  load(): void {
    this.loading.set(true);
    let pending = 2;
    const done = () => { if (--pending === 0) this.loading.set(false); };

    this.api.myTimeline(this.date).subscribe({
      next: (t) => { this.timeline.set(t); done(); },
      error: () => done(),
    });
    this.api.myDailyReport(this.date).subscribe({
      next: (r) => { this.report.set(r); done(); },
      error: () => done(),
    });
  }
}
