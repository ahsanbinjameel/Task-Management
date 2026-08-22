import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ApiService } from '../../core/api.service';
import { PagedResult, Priority, TaskSummaryDto, WorkTaskStatus } from '../../core/models';
import { EmptyComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';
import { TaskTableComponent } from '../../shared/task-table.component';
import { humanizeEnum } from '../../core/format';

const STATUSES: WorkTaskStatus[] = [
  'ReadyForAssignment', 'Assigned', 'ReadyToStart', 'InProgress', 'Paused', 'Blocked',
  'CompletedReadyForQC', 'QCReview', 'QCFailedRework', 'QCPassed', 'ReadyForClosure',
  'Closed', 'Cancelled', 'Deferred', 'OnHold', 'Reopened',
];

@Component({
  selector: 'app-task-list',
  standalone: true,
  imports: [
    FormsModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule,
    MatIconModule, MatPaginatorModule, MatSlideToggleModule,
    PageHeaderComponent, EmptyComponent, LoadingComponent, TaskTableComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Tasks" subtitle="Everything in the system you can see." />

      <div class="card card-pad filters">
        <mat-form-field class="search">
          <mat-label>Search</mat-label>
          <input matInput [(ngModel)]="search" (keyup.enter)="reload()"
                 placeholder="Title or task number" />
          <mat-icon matSuffix>search</mat-icon>
        </mat-form-field>

        <mat-form-field>
          <mat-label>Status</mat-label>
          <mat-select [(ngModel)]="status" (selectionChange)="reload()">
            <mat-option [value]="null">Any</mat-option>
            @for (s of statuses; track s) {
              <mat-option [value]="s">{{ label(s) }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field>
          <mat-label>Priority</mat-label>
          <mat-select [(ngModel)]="priority" (selectionChange)="reload()">
            <mat-option [value]="null">Any</mat-option>
            @for (p of priorities; track p) { <mat-option [value]="p">{{ p }}</mat-option> }
          </mat-select>
        </mat-form-field>

        <mat-slide-toggle [(ngModel)]="openOnly" (change)="reload()">Open only</mat-slide-toggle>
        <span class="spacer"></span>
        <button matButton (click)="reload()"><mat-icon>refresh</mat-icon> Refresh</button>
      </div>

      <div class="card list">
        @if (loading()) {
          <app-loading />
        } @else if (page().items.length === 0) {
          <app-empty message="No tasks match those filters" icon="search_off"
                     hint="Try clearing the status or search box." />
        } @else {
          <app-task-table [tasks]="page().items" (action)="open($event)" />
          <mat-paginator [length]="page().totalCount" [pageSize]="page().pageSize"
                         [pageIndex]="page().page - 1" [pageSizeOptions]="[25, 50, 100]"
                         (page)="onPage($event)" />
        }
      </div>
    </div>
  `,
  styles: `
    .filters { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; margin-bottom: 16px; }
    .filters mat-form-field { margin-bottom: -1.25em; }
    .search { flex: 1 1 260px; }
    .list { overflow: hidden; }
  `,
})
export class TaskListComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  readonly statuses = STATUSES;
  readonly priorities: Priority[] = ['Critical', 'High', 'Normal', 'Low'];

  search = '';
  status: WorkTaskStatus | null = null;
  priority: Priority | null = null;
  openOnly = true;

  readonly loading = signal(true);
  readonly page = signal<PagedResult<TaskSummaryDto>>(
    { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });

  private pageIndex = 0;
  private pageSize = 25;

  label = (value: string) => humanizeEnum(value);

  ngOnInit(): void { this.reload(); }

  reload(): void {
    this.loading.set(true);
    this.api.tasks({
      search: this.search || undefined,
      status: this.status ?? undefined,
      priority: this.priority ?? undefined,
      openOnly: this.openOnly,
      page: this.pageIndex + 1,
      pageSize: this.pageSize,
    }).subscribe({
      next: (result) => { this.page.set(result); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  onPage(event: PageEvent): void {
    this.pageIndex = event.pageIndex;
    this.pageSize = event.pageSize;
    this.reload();
  }

  open(task: TaskSummaryDto): void {
    void this.router.navigate(['/tasks', task.id]);
  }
}
