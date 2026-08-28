import { DestroyRef, Component, OnInit, inject, signal } from '@angular/core';
import { syncOn } from '../../core/realtime-sync';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { RealtimeService } from '../../core/realtime.service';
import { PagedResult, TaskSummaryDto } from '../../core/models';
import { PageHeaderComponent } from '../../shared/ui';
import { ViewTabsComponent } from '../../shared/view-tabs.component';
import { TaskTableComponent } from '../../shared/task-table.component';

/**
 * Work waiting for a reviewer. "Start review" claims the task, which is what stops two reviewers
 * from picking up the same thing — the server assigns the QC owner on the first claim.
 */
@Component({
  selector: 'app-qc-queue',
  standalone: true,
  imports: [
    MatButtonModule, MatIconModule, PageHeaderComponent, TaskTableComponent, ViewTabsComponent,
  ],
  template: `
    <div class="page fills">
      <app-page-header title="Quality checks">
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon> Refresh</button>
      </app-page-header>

      <app-view-tabs group="quality" />

      <app-task-table
        [tasks]="page().items"
        [columns]="['number', 'title', 'status', 'priority', 'assignee', 'worked', 'action']"
        actionLabel="Start review"
        [loading]="loading()"
        emptyMessage="Nothing waiting for QC" emptyIcon="verified"
        (action)="startReview($event)" />
    </div>
  `,
})
export class QcQueueComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly realtime = inject(RealtimeService);

  readonly loading = signal(true);
  readonly page = signal<PagedResult<TaskSummaryDto>>(
    { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });

  ngOnInit(): void {
   this.load();
    // Re-fetch on the server's say-so; see syncOn for why it debounces and tears down.
    syncOn(
      [this.realtime.taskChanged],
      () => this.load(),
      this.destroyRef);
  }

  load(): void {
    this.api.qcQueue().subscribe({
      next: (result) => { this.page.set(result); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  /**
   * Open the check. Claiming happens on the way in, once.
   *
   * The queue now also holds work somebody has already started checking — that is the point of the
   * fix, since it used to vanish the moment it was claimed while every other screen still called it
   * "Being checked". Claiming a second time is refused by the server, so a task already in review
   * is simply opened.
   */
  startReview(task: TaskSummaryDto): void {
    if (task.status === 'QCReview') {
      void this.router.navigate(['/tasks', task.id], { queryParams: { tab: 'qc' } });
      return;
    }

    this.api.startQC(task.id).subscribe(() => {
      this.toast.success(`${task.taskNumber} is now under your review.`);
      void this.router.navigate(['/tasks', task.id], { queryParams: { tab: 'qc' } });
    });
  }
}
