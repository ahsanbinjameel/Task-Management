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
    <div class="page">
      <app-page-header title="Quality checks">
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon> Refresh</button>
      </app-page-header>

      <app-view-tabs group="quality" />

      <app-task-table
        [tasks]="page().items"
        [columns]="['number', 'title', 'priority', 'assignee', 'worked', 'action']"
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

  startReview(task: TaskSummaryDto): void {
    this.api.startQC(task.id).subscribe(() => {
      this.toast.success(`${task.taskNumber} is now under your review.`);
      void this.router.navigate(['/tasks', task.id], { queryParams: { tab: 'qc' } });
    });
  }
}
