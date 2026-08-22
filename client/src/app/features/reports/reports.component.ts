import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTableModule } from '@angular/material/table';
import { ApiService } from '../../core/api.service';
import { DailyTeamReportDto } from '../../core/models';
import { DurationPipe, isoDate, saveBlob } from '../../core/format';
import {
  EmptyComponent, LoadingComponent, PageHeaderComponent, StatComponent,
} from '../../shared/ui';

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [
    FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatTableModule, PageHeaderComponent, StatComponent, EmptyComponent, LoadingComponent,
    DurationPipe,
  ],
  template: `
    <div class="page">
      <app-page-header title="Daily team report"
                       subtitle="Attendance and effort, from the same figures the timeline uses.">
        <mat-form-field class="date">
          <mat-label>Date</mat-label>
          <input matInput type="date" [(ngModel)]="date" (change)="load()" />
        </mat-form-field>
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon></button>
        <button matButton="filled" (click)="exportCsv()">
          <mat-icon>download</mat-icon> CSV
        </button>
      </app-page-header>

      @if (loading()) {
        <app-loading />
      } @else if (report(); as r) {
        <div class="stats">
          <app-stat label="On shift" [value]="r.peopleOnShift" />
          <app-stat label="Total shift time" [value]="(r.totalShiftTime | duration)" />
          <app-stat label="Productive" [value]="(r.totalProductiveTime | duration)" />
          <app-stat label="Tasks completed" [value]="r.tasksCompleted" />
        </div>

        <div class="card top-gap">
          @if (r.users.length === 0) {
            <app-empty message="Nobody was on shift that day" icon="event_busy" />
          } @else {
            <div class="table-scroll">
              <table mat-table [dataSource]="r.users">
                <ng-container matColumnDef="name">
                  <th mat-header-cell *matHeaderCellDef>Person</th>
                  <td mat-cell *matCellDef="let u"><strong>{{ u.displayName }}</strong></td>
                </ng-container>
                <ng-container matColumnDef="shift">
                  <th mat-header-cell *matHeaderCellDef>Shift</th>
                  <td mat-cell *matCellDef="let u" class="mono">{{ u.shiftDuration | duration }}</td>
                </ng-container>
                <ng-container matColumnDef="productive">
                  <th mat-header-cell *matHeaderCellDef>Productive</th>
                  <td mat-cell *matCellDef="let u" class="mono">{{ u.productiveTime | duration }}</td>
                </ng-container>
                <ng-container matColumnDef="break">
                  <th mat-header-cell *matHeaderCellDef>Away</th>
                  <td mat-cell *matCellDef="let u" class="mono">{{ u.breakTime | duration }}</td>
                </ng-container>
                <ng-container matColumnDef="worked">
                  <th mat-header-cell *matHeaderCellDef>Tasks worked</th>
                  <td mat-cell *matCellDef="let u" class="mono">{{ u.tasksWorked }}</td>
                </ng-container>
                <ng-container matColumnDef="completed">
                  <th mat-header-cell *matHeaderCellDef>Completed</th>
                  <td mat-cell *matCellDef="let u" class="mono">{{ u.tasksCompleted }}</td>
                </ng-container>
                <tr mat-header-row *matHeaderRowDef="columns"></tr>
                <tr mat-row *matRowDef="let row; columns: columns"></tr>
              </table>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: `
    .stats { display: grid; gap: 14px; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); }
    .top-gap { margin-top: 18px; }
    .date { width: 170px; margin-bottom: -1.25em; }
  `,
})
export class ReportsComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly report = signal<DailyTeamReportDto | null>(null);
  readonly loading = signal(true);
  readonly columns = ['name', 'shift', 'productive', 'break', 'worked', 'completed'];

  date = isoDate();

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.api.teamDailyReport(this.date).subscribe({
      next: (r) => { this.report.set(r); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  exportCsv(): void {
    this.api.teamDailyCsv(this.date)
      .subscribe((blob) => saveBlob(blob, `team-daily-${this.date}.csv`));
  }
}
