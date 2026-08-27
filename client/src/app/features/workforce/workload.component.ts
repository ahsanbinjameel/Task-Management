import { DestroyRef, Component, OnInit, inject, signal } from '@angular/core';
import { RealtimeService } from '../../core/realtime.service';
import { syncOn } from '../../core/realtime-sync';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../core/api.service';
import { WorkforceState, WorkloadDto } from '../../core/models';
import { workforceStateLabel } from '../../core/labels';
import { ChipComponent, PageHeaderComponent } from '../../shared/ui';
import { columnFilters } from '../../shared/column-filter.component';
import { DataGridComponent, GridCellDirective, GridColumn } from '../../shared/data-grid.component';

/** The states someone can be in, for the grid's filter. */
const WORKFORCE_STATES: WorkforceState[] = [
  'LoggedInShiftNotStarted', 'Available', 'Working', 'Break', 'Lunch', 'Meeting',
  'TemporarilyAway', 'ShiftEnded',
];

@Component({
  selector: 'app-workload',
  standalone: true,
  imports: [
    RouterLink, MatButtonModule, MatIconModule, PageHeaderComponent, ChipComponent,
    DataGridComponent, GridCellDirective,
  ],
  template: `
    <div class="page">
      <app-page-header title="Workload">
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon> Refresh</button>
      </app-page-header>

      <!-- Unpaged: every row is already here, so filtering locally cannot mislead. -->
      <app-data-grid
        mode="local"
        [rows]="rows()" [columns]="columns" [loading]="loading()" [filters]="filters"
        emptyMessage="Nobody has open work" emptyIcon="groups"
        emptyActionLabel="Assignment queue" emptyActionRoute="/assignment"
        noMatchesMessage="Nobody matches those filters.">

        <ng-template gridCell="name" let-r><strong>{{ r.displayName }}</strong></ng-template>

        <ng-template gridCell="state" let-r>
          <app-chip [value]="r.workforceState" kind="workforce" [dot]="true" />
        </ng-template>

        <ng-template gridCell="blocked" let-r>
          <span [class.overdue]="r.blockedCount > 0">{{ r.blockedCount }}</span>
        </ng-template>

        <ng-template gridCell="now" let-r>
          @if (r.activeTaskNumber) {
            <a class="grid-link mono" [routerLink]="['/tasks', r.activeTaskId]">
              {{ r.activeTaskNumber }}
            </a>
          } @else { <span class="muted">—</span> }
        </ng-template>
      </app-data-grid>
    </div>
  `,
})
export class WorkloadComponent implements OnInit {
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(ApiService);

  readonly rows = signal<WorkloadDto[]>([]);
  readonly loading = signal(true);

  readonly filters = columnFilters(() => undefined);

  readonly columns: GridColumn<WorkloadDto>[] = [
    {
      key: 'name', header: 'Person', sortable: true, minWidth: 180,
      cell: (r) => r.displayName,
      filter: { kind: 'text', placeholder: 'Name' },
    },
    {
      key: 'state', header: 'Availability', sortable: true,
      cell: (r) => r.workforceState,
      filter: {
        kind: 'select', placeholder: 'Any',
        options: WORKFORCE_STATES.map((v) => ({ value: v, label: workforceStateLabel(v) })),
      },
    },
    { key: 'open', header: 'Open', sortable: true, cellClass: 'mono', cell: (r) => r.openTaskCount },
    {
      key: 'running', header: 'In progress', sortable: true, cellClass: 'mono',
      cell: (r) => r.inProgressCount,
    },
    {
      key: 'blocked', header: 'Cannot continue', sortable: true, cellClass: 'mono',
      cell: (r) => r.blockedCount,
    },
    {
      key: 'hours', header: 'Outstanding', sortable: true, cellClass: 'mono',
      cell: (r) => `${r.estimatedHoursOutstanding}h`,
      sortValue: (r) => r.estimatedHoursOutstanding,
    },
    { key: 'now', header: 'On now', cell: (r) => r.activeTaskNumber },
  ];

  ngOnInit(): void {
    this.load();
    // Re-fetch on the server's say-so; see syncOn for why it debounces and tears down.
    syncOn(
      [this.realtime.taskChanged, this.realtime.workforceChanged],
      () => this.load(),
      this.destroyRef);
  }

  load(): void {
    this.api.workload().subscribe({
      next: (rows) => { this.rows.set(rows); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }
}
