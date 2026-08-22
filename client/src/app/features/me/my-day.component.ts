import { Component, OnInit, inject, signal } from '@angular/core';
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
      <app-page-header title="My day" subtitle="Your shift, your availability, and where the time went.">
        <mat-form-field class="date">
          <mat-label>Date</mat-label>
          <input matInput type="date" [(ngModel)]="date" (change)="load()" />
        </mat-form-field>
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon></button>
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
          </div>
        }

        <div class="two-col top-gap">
          <div class="card">
            <div class="card-pad"><h2 class="card-title" style="margin:0">Timeline</h2></div>
            @if (!timeline() || timeline()!.entries.length === 0) {
              <app-empty message="No activity recorded" icon="schedule"
                         hint="Start a shift to begin tracking." />
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
            <div class="card-pad"><h2 class="card-title" style="margin:0">Where the time went</h2></div>
            @if (!report() || report()!.breakdown.length === 0) {
              <app-empty message="No time logged against tasks" icon="timer_off" />
            } @else {
              @for (line of report()!.breakdown; track line.taskId) {
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
        </div>
      }
    </div>
  `,
  styles: `
    .stats { display: grid; gap: 14px; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); }
    .two-col { display: grid; gap: 16px; grid-template-columns: repeat(auto-fit, minmax(340px, 1fr)); }
    .top-gap { margin-top: 18px; }
    .date { width: 170px; margin-bottom: -1.25em; }
    .entry {
      display: flex; align-items: center; gap: 12px;
      padding: 9px 20px; border-top: 1px solid var(--border);
      color: inherit; text-decoration: none; font-size: 13.5px;
    }
    a.entry:hover { background: var(--surface-sunken); }
    .time { flex: 0 0 auto; color: var(--text-muted); min-width: 88px; }
    .label { min-width: 0; }
  `,
})
export class MyDayComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly timeline = signal<DailyTimelineDto | null>(null);
  readonly report = signal<DailyUserReportDto | null>(null);
  readonly loading = signal(true);

  date = isoDate();

  ngOnInit(): void { this.load(); }

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
