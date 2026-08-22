import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { ApiService } from '../../core/api.service';
import { WorkloadDto } from '../../core/models';
import { ChipComponent, EmptyComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';

@Component({
  selector: 'app-workload',
  standalone: true,
  imports: [
    RouterLink, MatButtonModule, MatIconModule, MatTableModule,
    PageHeaderComponent, ChipComponent, EmptyComponent, LoadingComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Workload" subtitle="Open work per person, and what they are on now.">
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon> Refresh</button>
      </app-page-header>

      <div class="card">
        @if (loading()) {
          <app-loading />
        } @else if (rows().length === 0) {
          <app-empty message="Nobody has open work" icon="groups" />
        } @else {
          <div class="table-scroll">
            <table mat-table [dataSource]="rows()">
              <ng-container matColumnDef="name">
                <th mat-header-cell *matHeaderCellDef>Person</th>
                <td mat-cell *matCellDef="let r"><strong>{{ r.displayName }}</strong></td>
              </ng-container>

              <ng-container matColumnDef="state">
                <th mat-header-cell *matHeaderCellDef>Availability</th>
                <td mat-cell *matCellDef="let r">
                  <app-chip [value]="r.workforceState" kind="workforce" [dot]="true" />
                </td>
              </ng-container>

              <ng-container matColumnDef="open">
                <th mat-header-cell *matHeaderCellDef>Open</th>
                <td mat-cell *matCellDef="let r" class="mono">{{ r.openTaskCount }}</td>
              </ng-container>

              <ng-container matColumnDef="running">
                <th mat-header-cell *matHeaderCellDef>In progress</th>
                <td mat-cell *matCellDef="let r" class="mono">{{ r.inProgressCount }}</td>
              </ng-container>

              <ng-container matColumnDef="blocked">
                <th mat-header-cell *matHeaderCellDef>Blocked</th>
                <td mat-cell *matCellDef="let r" class="mono"
                    [class.overdue]="r.blockedCount > 0">{{ r.blockedCount }}</td>
              </ng-container>

              <ng-container matColumnDef="hours">
                <th mat-header-cell *matHeaderCellDef>Outstanding</th>
                <td mat-cell *matCellDef="let r" class="mono">{{ r.estimatedHoursOutstanding }}h</td>
              </ng-container>

              <ng-container matColumnDef="now">
                <th mat-header-cell *matHeaderCellDef>On now</th>
                <td mat-cell *matCellDef="let r">
                  @if (r.activeTaskNumber) {
                    <a class="mono small" [routerLink]="['/tasks', r.activeTaskId]">
                      {{ r.activeTaskNumber }}
                    </a>
                  } @else { <span class="muted">—</span> }
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="columns"></tr>
              <tr mat-row *matRowDef="let row; columns: columns"></tr>
            </table>
          </div>
        }
      </div>
    </div>
  `,
})
export class WorkloadComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly rows = signal<WorkloadDto[]>([]);
  readonly loading = signal(true);
  readonly columns = ['name', 'state', 'open', 'running', 'blocked', 'hours', 'now'];

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.workload().subscribe({
      next: (rows) => { this.rows.set(rows); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }
}
