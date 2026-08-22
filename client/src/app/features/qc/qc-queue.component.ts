import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { RealtimeService } from '../../core/realtime.service';
import { PagedResult, TaskSummaryDto } from '../../core/models';
import { EmptyComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';
import { TaskTableComponent } from '../../shared/task-table.component';

/**
 * Work waiting for a reviewer. "Start review" claims the task, which is what stops two reviewers
 * from picking up the same thing — the server assigns the QC owner on the first claim.
 */
@Component({
  selector: 'app-qc-queue',
  standalone: true,
  imports: [
    MatButtonModule, MatIconModule, PageHeaderComponent, EmptyComponent, LoadingComponent,
    TaskTableComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="QC queue" subtitle="Completed work waiting for review.">
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon> Refresh</button>
      </app-page-header>

      <div class="card">
        @if (loading()) {
          <app-loading />
        } @else if (page().items.length === 0) {
          <app-empty message="Nothing waiting for QC" icon="verified"
                     hint="Tasks arrive here when an assignee marks them complete." />
        } @else {
          <app-task-table
            [tasks]="page().items"
            [columns]="['number', 'title', 'priority', 'assignee', 'worked', 'action']"
            actionLabel="Start review"
            (action)="startReview($event)" />
        }
      </div>
    </div>
  `,
})
export class QcQueueComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly realtime = inject(RealtimeService);

  readonly loading = signal(true);
  readonly page = signal<PagedResult<TaskSummaryDto>>(
    { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });

  ngOnInit(): void {
    this.load();
    this.realtime.taskChanged.subscribe(() => this.load());
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
