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
import { ViewTabsComponent } from '../../shared/view-tabs.component';
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
    MatButtonModule, MatIconModule, PageHeaderComponent, TaskTableComponent, ViewTabsComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Assignment queue">
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon> Refresh</button>
      </app-page-header>

      <app-view-tabs group="tasks" />

      <div class="layout">
        <app-task-table
          [tasks]="page().items"
          [columns]="['number', 'title', 'priority', 'due', 'action']"
          actionLabel="Assign"
          [loading]="loading()"
          emptyMessage="Everything is assigned" emptyIcon="done_all"
          (action)="assign($event)" />

        <!--
          "Who has capacity" with a summed-estimate figure beside each name is gone
          (PRODUCT-CORE §12C). It was the one number on this screen nobody could act on: most tasks
          carry no estimate at all, and adding the guesses that exist together does not make a
          fact. What is left is what a coordinator can actually check — who is on the clock, what
          they are running this minute, and how much is already queued behind it. The fuller
          picture, including whether they have worked on this part of the product before, is in the
          assign dialog, because that is where the choice is made.
        -->
        <aside class="card card-pad">
          <h2 class="card-title">Who is around</h2>
          @for (person of workload(); track person.userId) {
            <div class="person">
              <span class="dot" [class.on]="person.isOnShift"></span>
              <div class="who">
                <strong class="truncate">{{ person.displayName }}</strong>
                <span class="muted small">
                  @if (person.activeTaskNumber) {
                    <span class="now">On {{ person.activeTaskNumber }}</span>
                  } @else if (!person.isOnShift) {
                    Not on shift
                  } @else {
                    Free
                  }
                  · {{ person.openTaskCount }} open
                  @if (person.blockedCount > 0) { · {{ person.blockedCount }} blocked }
                </span>
              </div>
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
    .dot {
      width: 8px; height: 8px; border-radius: 50%; flex: none;
      border: 1.5px solid var(--border-strong); background: transparent;
    }
    .dot.on { background: var(--tone-good-fg); border-color: var(--tone-good-fg); }
    .now { color: var(--tone-running-fg); font-weight: 500; }
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
