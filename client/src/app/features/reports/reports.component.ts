import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../../core/api.service';
import { DailyTeamReportDto, DailyUserReportDto } from '../../core/models';
import { DurationPipe, isoDate, saveBlob } from '../../core/format';
import { MatDialog } from '@angular/material/dialog';
import { openPdf } from '../../shared/pdf-viewer.component';
import {
  ColumnFilterComponent, ColumnFilterSpec, NoMatchesComponent, columnFilters,
} from '../../shared/column-filter.component';
import {
  EmptyComponent, LoadingComponent, PageHeaderComponent, StatComponent,
} from '../../shared/ui';

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [
    FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatTableModule, MatTooltipModule, PageHeaderComponent, StatComponent, EmptyComponent, LoadingComponent,
    DurationPipe,
      ColumnFilterComponent,
      NoMatchesComponent,
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
        <button matButton (click)="exportCsv()">
          <mat-icon>table_view</mat-icon> CSV
        </button>
        <button matButton="filled" (click)="viewPdf()">
          <mat-icon>picture_as_pdf</mat-icon> View PDF
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
            <app-empty message="Nobody was on shift that day" icon="event_busy"
                       hint="Pick another date, or check that people are starting their shifts." />
          } @else {
            <div class="table-scroll">
              <table mat-table [dataSource]="visible(r.users)">
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
                <!--
                  Quick work earns a column of its own. Folded into "productive" it would be
                  invisible, and invisible is exactly what it was before.
                -->
                <ng-container matColumnDef="quick">
                  <th mat-header-cell *matHeaderCellDef>Quick work</th>
                  <td mat-cell *matCellDef="let u" class="mono">
                    {{ u.quickWorkTime | duration }}
                    @if (u.interruptions > 0) {
                      <span class="muted small">· {{ u.interruptions }} interrupted</span>
                    }
                  </td>
                </ng-container>
                <ng-container matColumnDef="pdf">
                  <th mat-header-cell *matHeaderCellDef aria-label="Download"></th>
                  <td mat-cell *matCellDef="let u">
                    <button matIconButton (click)="viewPerson(u)"
                            [attr.aria-label]="'Open ' + u.displayName + ' as PDF'"
                            matTooltip="This person's day as a PDF">
                      <mat-icon>picture_as_pdf</mat-icon>
                    </button>
                  </td>
                </ng-container>
                <!--
                  Filtered here rather than on the server, and correctly so: this endpoint returns
                  the whole day for the whole team in one unpaged response, so the rows on screen
                  are all the rows there are. Narrowing them locally cannot lie about a total the
                  way it would on a paged grid.
                -->
                @for (column of columns; track column) {
                  <ng-container [matColumnDef]="column + '_filter'">
                    <th mat-header-cell *matHeaderCellDef class="filter-cell">
                      <app-column-filter [spec]="specs[column]" [value]="filters.value(column)"
                                         (changed)="filters.set(specs[column], column, $event)" />
                    </th>
                  </ng-container>
                }

                <tr mat-header-row *matHeaderRowDef="columns"></tr>
                <tr mat-header-row *matHeaderRowDef="filterRow" class="filter-row"></tr>
                <tr mat-row *matRowDef="let row; columns: columns"></tr>
              </table>
            </div>

            @if (visible(r.users).length === 0) {
              <app-no-matches message="Nobody on that day matches those filters."
                              (clear)="filters.clear()" />
            }
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
  private readonly dialog = inject(MatDialog);

  readonly report = signal<DailyTeamReportDto | null>(null);
  readonly loading = signal(true);
  readonly columns = ['name', 'shift', 'productive', 'break', 'worked', 'completed', 'quick', 'pdf'];
  readonly filterRow = this.columns.map((c) => c + '_filter');

  /** Local, because the report arrives whole — see the note in the template. */
  readonly filters = columnFilters(() => undefined);

  readonly specs: Record<string, ColumnFilterSpec> = {
    name: { key: 'name', kind: 'text', placeholder: 'Name' },
  };

  visible(users: DailyUserReportDto[]): DailyUserReportDto[] {
    const name = this.filters.value('name').trim().toLowerCase();
    if (!name) return users;
    return users.filter((u) => u.displayName.toLowerCase().includes(name));
  }

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
