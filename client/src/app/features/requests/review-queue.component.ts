import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { RealtimeService } from '../../core/realtime.service';
import { PagedResult, RequestSummaryDto } from '../../core/models';
import { ChipComponent, EmptyComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';

/**
 * The reviewer's queue, ordered by the server: urgency first, then oldest. Starting a review claims
 * the request, which is what keeps two reviewers off the same item.
 */
@Component({
  selector: 'app-review-queue',
  standalone: true,
  imports: [
    DatePipe, RouterLink, MatButtonModule, MatIconModule,
    PageHeaderComponent, ChipComponent, EmptyComponent, LoadingComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Review queue"
                       subtitle="Most urgent first, then oldest. Five of the six outcomes create no work at all.">
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon> Refresh</button>
      </app-page-header>

      <div class="card">
        @if (loading()) {
          <app-loading />
        } @else if (page().items.length === 0) {
          <app-empty message="Nothing waiting for review" icon="rate_review"
                     hint="New requests land here as soon as they are submitted." />
        } @else {
          @for (request of page().items; track request.id) {
            <div class="item">
              <div class="body">
                <div class="line1">
                  <a class="mono small muted" [routerLink]="['/requests', request.id]">
                    {{ request.requestNumber }}
                  </a>
                  <strong class="truncate">{{ request.title }}</strong>
                </div>
                <div class="line2">
                  <app-chip [value]="request.requestedUrgency" kind="priority" />
                  <app-chip [value]="request.status" />
                  <span class="muted small">
                    {{ request.requestedByDisplayName }} · {{ request.requestedAt | date: 'MMM d' }}
                  </span>
                  @if (request.attachmentCount > 0) {
                    <span class="muted small">
                      <mat-icon class="tiny">attach_file</mat-icon> {{ request.attachmentCount }}
                    </span>
                  }
                </div>
              </div>
              <button matButton="filled" (click)="review(request)">Review</button>
            </div>
          }
        }
      </div>
    </div>
  `,
  styles: `
    .item {
      display: flex; align-items: center; gap: 14px;
      padding: 14px 18px; border-bottom: 1px solid var(--border);
    }
    .item:last-child { border-bottom: none; }
    .body { flex: 1 1 auto; min-width: 0; }
    .line1 { display: flex; align-items: center; gap: 9px; }
    .line2 { display: flex; align-items: center; gap: 9px; margin-top: 5px; flex-wrap: wrap; }
    .tiny { font-size: 14px; width: 14px; height: 14px; vertical-align: text-bottom; }
  `,
})
export class ReviewQueueComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly realtime = inject(RealtimeService);

  readonly loading = signal(true);
  readonly page = signal<PagedResult<RequestSummaryDto>>(
    { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });

  ngOnInit(): void {
    this.load();
    this.realtime.requestChanged.subscribe(() => this.load());
  }

  load(): void {
    this.api.reviewQueue(1, 50).subscribe({
      next: (result) => { this.page.set(result); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  review(request: RequestSummaryDto): void {
    // Already in review (by us) is fine; the API is idempotent about it.
    this.api.startReview(request.id).subscribe({
      next: () => void this.router.navigate(['/requests', request.id]),
      error: () => void this.router.navigate(['/requests', request.id]),
    });
  }
}
