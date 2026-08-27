import { DestroyRef, Component, OnInit, inject, signal } from '@angular/core';
import { syncOn } from '../../core/realtime-sync';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { RealtimeService } from '../../core/realtime.service';
import { PagedResult, TaskSummaryDto, WorkloadDto } from '../../core/models';
import { PageHeaderComponent } from '../../shared/ui';
import { TaskTableComponent } from '../../shared/task-table.component';
import { AssignDialogComponent, AssignDialogResult } from './assign-dialog.component';

/**
 * Approved work with nobody on it, plus a live read of who has capacity — the two things a
 * coordinator needs on one screen, because deciding *who* is the whole job.
 */
@Component({
  selector: 'app-assignment-queue',
  standalone: true,
  imports: [
    MatButtonModule, MatIconModule, PageHeaderComponent, TaskTableComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Assignment queue">
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon> Refresh</button>
      </app-page-header>

      <div class="layout">
        <app-task-table
          [tasks]="page().items"
          [columns]="['number', 'title', 'priority', 'due', 'action']"
          actionLabel="Assign"
          [loading]="loading()"
          emptyMessage="Everything is assigned" emptyIcon="done_all"
          (action)="assign($event)" />

        <aside class="card card-pad">
          <h2 class="card-title">Who has capacity</h2>
          @for (person of workload(); track person.userId) {
            <div class="person">
              <div class="who">
                <strong class="truncate">{{ person.displayName }}</strong>
                <span class="muted small">
                  {{ person.openTaskCount }} open
                  @if (person.blockedCount > 0) { · {{ person.blockedCount }} blocked }
                </span>
              </div>
              <span class="load mono small">{{ person.estimatedHoursOutstanding }}h</span>
            </div>
          } @empty {
            <p class="muted small">Nobody has open work.</p>
          }
        </aside>
      </div>
    </div>
  `,
  styles: `
    .layout { display: grid; gap: 16px; grid-template-columns: minmax(0, 1fr) 290px; }
    @media (max-width: 1100px) { .layout { grid-template-columns: 1fr; } }
    .person {
      display: flex; align-items: center; flex-wrap: wrap; gap: 10px;
      padding: 9px 0; border-top: 1px solid var(--border);
    }
    .person:first-of-type { border-top: none; }
    .who { flex: 1 1 auto; min-width: 0; display: flex; flex-direction: column; }
    .load { color: var(--text-muted); }
  `,
})
export class AssignmentQueueComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly realtime = inject(RealtimeService);

  readonly loading = signal(true);
  readonly page = signal<PagedResult<TaskSummaryDto>>(
    { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });
  readonly workload = signal<WorkloadDto[]>([]);

  ngOnInit(): void {
   this.load();
    // Re-fetch on the server's say-so; see syncOn for why it debounces and tears down.
    syncOn(
      [this.realtime.taskChanged],
      () => this.load(),
      this.destroyRef);
  }

  load(): void {
    this.api.assignmentQueue(1, 50).subscribe({
      next: (result) => { this.page.set(result); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
    this.api.workload().subscribe({ next: (w) => this.workload.set(w), error: () => undefined });
  }

  assign(task: TaskSummaryDto): void {
    this.dialog
      .open(AssignDialogComponent, {
        data: { task, isReassign: false, currentAssigneeId: task.primaryAssigneeUserId },
      })
      .afterClosed()
      .subscribe((assigned?: AssignDialogResult) => {
        // The dialog performs the assignment and only closes once it has succeeded, so reaching
        // here means it is already saved.
        if (!assigned) return;
        this.toast.success(`${task.taskNumber} is now with ${assigned.primaryAssigneeDisplayName ?? 'the queue'}.`);
        this.load();
      });
  }
}
