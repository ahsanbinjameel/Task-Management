import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../../core/api.service';
import { DailyTeamReportDto, DailyUserReportDto } from '../../core/models';
import { DurationPipe, isoDate, parseTimeSpan, saveBlob } from '../../core/format';
import { MatDialog } from '@angular/material/dialog';
import { openPdf } from '../../shared/pdf-viewer.component';
import { columnFilters } from '../../shared/column-filter.component';
import { DataGridComponent, GridCellDirective, GridColumn } from '../../shared/data-grid.component';
import { LoadingComponent, PageHeaderComponent, StatComponent } from '../../shared/ui';
import { ViewTabsComponent } from '../../shared/view-tabs.component';

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [
    FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatTooltipModule, PageHeaderComponent, StatComponent, LoadingComponent, DurationPipe,
    DataGridComponent, GridCellDirective, ViewTabsComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Daily team report">
        <mat-form-field class="date">
          <mat-label>Date</mat-label>
          <input matInput type="date" [(ngModel)]="date" (change)="load()" />
        </mat-form-field>
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon></button>
        <button matButton (click)="exportCsv()">
          <mat-icon>table_view</mat-icon> CSV
        </button>
        <button matButton="filled" (click)="viewPdf()">
          <mat-icon>picture_as_pdf</mat-icon> View PDF
        </button>
      </app-page-header>

      <app-view-tabs group="team" />

      @if (loading()) {
        <app-loading />
      } @else if (report(); as r) {
        <div class="stats">
          <app-stat label="On shift" [value]="r.peopleOnShift" />
          <app-stat label="Total shift time" [value]="(r.totalShiftTime | duration)" />
          <app-stat label="Productive" [value]="(r.totalProductiveTime | duration)" />
          <app-stat label="Tasks completed" [value]="r.tasksCompleted" />
        </div>

        <!--
          Filtered in the grid rather than on the server, and correctly so: this endpoint returns
          the whole day for the whole team in one unpaged response, so the rows on screen are all
          the rows there are. Narrowing them locally cannot lie about a total the way it would on a
          paged grid.
        -->
        <div class="top-gap">
          <app-data-grid
            mode="local"
            [rows]="r.users" [columns]="columns" [filters]="filters"
            emptyMessage="Nobody was on shift that day" emptyIcon="event_busy"
            noMatchesMessage="Nobody on that day matches those filters.">

            <ng-template gridCell="name" let-u><strong>{{ u.displayName }}</strong></ng-template>

            <ng-template gridCell="shift" let-u>{{ u.shiftDuration | duration }}</ng-template>
            <ng-template gridCell="productive" let-u>{{ u.productiveTime | duration }}</ng-template>
            <ng-template gridCell="break" let-u>{{ u.breakTime | duration }}</ng-template>

            <!--
              Quick work earns a column of its own. Folded into "productive" it would be invisible,
              and invisible is exactly what it was before.
            -->
            <ng-template gridCell="quick" let-u>
              {{ u.quickWorkTime | duration }}
              @if (u.interruptions > 0) {
                <span class="muted small">· {{ u.interruptions }} interrupted</span>
              }
            </ng-template>

            <ng-template gridCell="pdf" let-u>
              <button matIconButton (click)="viewPerson(u)"
                      [attr.aria-label]="'Open ' + u.displayName + ' as PDF'"
                      matTooltip="This person's day as a PDF">
                <mat-icon>picture_as_pdf</mat-icon>
              </button>
            </ng-template>
          </app-data-grid>
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
  private readonly dialog = inject(MatDialog);

  readonly report = signal<DailyTeamReportDto | null>(null);
  readonly loading = signal(true);

  /** Local, because the report arrives whole — see the note in the template. */
  readonly filters = columnFilters(() => undefined);

  readonly columns: GridColumn<DailyUserReportDto>[] = [
    {
      key: 'name', header: 'Person', sortable: true, minWidth: 180,
      cell: (u) => u.displayName,
      filter: { kind: 'text', placeholder: 'Name' },
    },
    {
      key: 'shift', header: 'Shift', sortable: true, cellClass: 'mono',
      sortValue: (u) => parseTimeSpan(u.shiftDuration),
    },
    {
      key: 'productive', header: 'Productive', sortable: true, cellClass: 'mono',
      sortValue: (u) => parseTimeSpan(u.productiveTime),
    },
    {
      key: 'break', header: 'Away', sortable: true, cellClass: 'mono',
      sortValue: (u) => parseTimeSpan(u.breakTime),
    },
    {
      key: 'worked', header: 'Tasks worked', sortable: true, cellClass: 'mono',
      cell: (u) => u.tasksWorked,
    },
    {
      key: 'completed', header: 'Completed', sortable: true, cellClass: 'mono',
      cell: (u) => u.tasksCompleted,
    },
    {
      key: 'quick', header: 'Quick work', sortable: true, cellClass: 'mono',
      sortValue: (u) => parseTimeSpan(u.quickWorkTime),
    },
    { key: 'pdf', header: 'Download', headerHidden: true, align: 'right' },
  ];

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

  /**
   * Opened, not collected. The CSV button still downloads — a spreadsheet is taken away to be
   * worked on, whereas a PDF of today is nearly always read once and closed.
   */
  viewPdf(): void {
    openPdf(this.dialog, {
      title: `Team day — ${this.date}`,
      fileName: `team-daily-${this.date}.pdf`,
      load: () => this.api.teamDailyPdf(this.date),
    });
  }

  viewPerson(user: DailyUserReportDto): void {
    openPdf(this.dialog, {
      title: `${user.displayName} — ${this.date}`,
      fileName: `${user.displayName.replace(/\s+/g, '-')}-${this.date}.pdf`,
      load: () => this.api.userDailyPdf(user.userId, this.date),
    });
  }
}
