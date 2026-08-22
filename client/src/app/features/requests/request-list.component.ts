import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { Perm } from '../../core/permissions';
import { PagedResult, RequestStatus, RequestSummaryDto } from '../../core/models';
import { humanizeEnum } from '../../core/format';
import { ChipComponent, EmptyComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';

const STATUSES: RequestStatus[] = [
  'Submitted', 'InReview', 'ClarificationRequired', 'Approved', 'Rejected', 'Duplicate',
  'Deferred', 'Escalated',
];

@Component({
  selector: 'app-request-list',
  standalone: true,
  imports: [
    DatePipe, FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatSelectModule, MatSlideToggleModule, MatTableModule, MatPaginatorModule,
    MatTooltipModule, PageHeaderComponent, ChipComponent, EmptyComponent, LoadingComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Requests" subtitle="Everything that has been asked for.">
        @if (auth.has(Perm.requestCreate)) {
          <a matButton="filled" routerLink="/requests/new">
            <mat-icon>add</mat-icon> New request
          </a>
        }
      </app-page-header>

      <div class="card card-pad filters">
        <mat-form-field class="search">
          <mat-label>Search</mat-label>
          <input matInput [(ngModel)]="search" (keyup.enter)="reload()" />
          <mat-icon matSuffix>search</mat-icon>
        </mat-form-field>

        <mat-form-field>
          <mat-label>Status</mat-label>
          <mat-select [(ngModel)]="status" (selectionChange)="reload()">
            <mat-option [value]="null">Any</mat-option>
            @for (s of statuses; track s) { <mat-option [value]="s">{{ label(s) }}</mat-option> }
          </mat-select>
        </mat-form-field>

        @if (auth.has(Perm.requestViewAll)) {
          <mat-slide-toggle [(ngModel)]="mine" (change)="reload()">Only mine</mat-slide-toggle>
        }
        <span class="spacer"></span>
        <button matButton (click)="reload()"><mat-icon>refresh</mat-icon> Refresh</button>
      </div>

      <div class="card">
        @if (loading()) {
          <app-loading />
        } @else if (page().items.length === 0) {
          <app-empty message="No requests found" icon="inbox" />
        } @else {
          <div class="table-scroll">
            <table mat-table [dataSource]="page().items">
              <ng-container matColumnDef="number">
                <th mat-header-cell *matHeaderCellDef>Request</th>
                <td mat-cell *matCellDef="let r">
                  <a class="mono link" [routerLink]="['/requests', r.id]">{{ r.requestNumber }}</a>
                </td>
              </ng-container>

              <ng-container matColumnDef="title">
                <th mat-header-cell *matHeaderCellDef>Title</th>
                <td mat-cell *matCellDef="let r">
                  <a class="title" [routerLink]="['/requests', r.id]">{{ r.title }}</a>
                  @if (r.hasOpenClarification) {
                    <mat-icon class="flag" matTooltip="Waiting on a clarification">help</mat-icon>
                  }
                </td>
              </ng-container>

              <ng-container matColumnDef="status">
                <th mat-header-cell *matHeaderCellDef>Status</th>
                <td mat-cell *matCellDef="let r"><app-chip [value]="r.status" /></td>
              </ng-container>

              <ng-container matColumnDef="urgency">
                <th mat-header-cell *matHeaderCellDef>Urgency</th>
                <td mat-cell *matCellDef="let r">
                  <app-chip [value]="r.requestedUrgency" kind="priority" />
                </td>
              </ng-container>

              <ng-container matColumnDef="requester">
                <th mat-header-cell *matHeaderCellDef>Raised by</th>
                <td mat-cell *matCellDef="let r">{{ r.requestedByDisplayName }}</td>
              </ng-container>

              <ng-container matColumnDef="raised">
                <th mat-header-cell *matHeaderCellDef>Raised</th>
                <td mat-cell *matCellDef="let r" class="nowrap">
                  {{ r.requestedAt | date: 'MMM d' }}
                </td>
              </ng-container>

              <ng-container matColumnDef="task">
                <th mat-header-cell *matHeaderCellDef>Task</th>
                <td mat-cell *matCellDef="let r">
                  @if (r.generatedTaskId) {
                    <a class="link mono small" [routerLink]="['/tasks', r.generatedTaskId]">Open</a>
                  } @else { <span class="muted">—</span> }
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="columns"></tr>
              <tr mat-row *matRowDef="let row; columns: columns"></tr>
            </table>
          </div>
          <mat-paginator [length]="page().totalCount" [pageSize]="page().pageSize"
                         [pageIndex]="page().page - 1" [pageSizeOptions]="[25, 50]"
                         (page)="onPage($event)" />
        }
      </div>
    </div>
  `,
  styles: `
    .filters { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; margin-bottom: 16px; }
    .filters mat-form-field { margin-bottom: -1.25em; }
    .search { flex: 1 1 240px; }
    .link { color: var(--text-muted); text-decoration: none; }
    .link:hover { text-decoration: underline; }
    .title { color: inherit; text-decoration: none; font-weight: 500; }
    .title:hover { text-decoration: underline; }
    .flag {
      font-size: 15px; width: 15px; height: 15px;
      color: var(--tone-warn-fg); vertical-align: middle; margin-left: 5px;
    }
  `,
})
export class RequestListComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  readonly Perm = Perm;
  readonly statuses = STATUSES;

  readonly columns = ['number', 'title', 'status', 'urgency', 'requester', 'raised', 'task'];

  search = '';
  status: RequestStatus | null = null;
  mine = false;

  readonly loading = signal(true);
  readonly page = signal<PagedResult<RequestSummaryDto>>(
    { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });

  private pageIndex = 0;
  private pageSize = 25;

  label = (value: string) => humanizeEnum(value);

  ngOnInit(): void {
    // Someone without ViewAll only ever sees their own; no point offering the toggle.
    this.mine = !this.auth.has(Perm.requestViewAll);
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.api.requests({
      search: this.search || undefined,
      status: this.status ?? undefined,
      mine: this.mine || undefined,
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
}
