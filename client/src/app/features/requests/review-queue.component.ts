import { DestroyRef, Component, OnInit, inject, signal } from '@angular/core';
import { syncOn } from '../../core/realtime-sync';
import { DatePipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { RealtimeService } from '../../core/realtime.service';
import { PagedResult, RequestBatchSummaryDto, RequestSummaryDto } from '../../core/models';
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
      <app-page-header title="Review queue">
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon> Refresh</button>
      </app-page-header>

      <!--
        Submissions that arrived as a set, above the individual requests.
        They are listed separately rather than flattened in among them because the decision they
        invite is a different one: "which of these eight are the same job" cannot be made from a
        list where the eight are scattered between unrelated requests. The items are still in the
        list below as well — each is an ordinary request and can be reviewed on its own.
      -->
      @if (batches().length > 0) {
        <div class="card batches">
          <div class="card-pad">
            <h2 class="card-title" style="margin:0">Asked for as a set</h2>
            <p class="muted small" style="margin:3px 0 0">
              Several things submitted together. Open one to decide on the items, or to combine
              some of them into a single piece of work.
            </p>
          </div>
          @for (batch of batches(); track batch.id) {
            <a class="item batch" [routerLink]="['/requests/batches', batch.id]">
              <div class="body">
                <div class="line1">
                  <span class="mono small muted">{{ batch.batchNumber }}</span>
                  <strong class="truncate">{{ batch.title }}</strong>
                </div>
                <div class="line2">
                  <span class="chip tone-warn">{{ batch.awaitingDecisionCount }} waiting</span>
                  @if (batch.approvedCount > 0) {
                    <span class="chip tone-good">{{ batch.approvedCount }} approved</span>
                  }
                  @if (batch.declinedCount > 0) {
                    <span class="chip tone-muted">{{ batch.declinedCount }} declined</span>
                  }
                  <span class="muted small">
                    {{ batch.requestedByDisplayName }} · {{ batch.requestedAt | date: 'MMM d' }}
                    @if (batch.clientName) { · {{ batch.clientName }} }
                  </span>
                </div>
              </div>
              <mat-icon>chevron_right</mat-icon>
            </a>
          }
        </div>
      }

      <div class="card">
        @if (loading()) {
          <app-loading />
        } @else if (page().items.length === 0) {
          <app-empty message="Nothing waiting for review" icon="rate_review" />
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
                  <app-chip [value]="request.requestedUrgency" kind="urgency" />
                  <app-chip [value]="request.status" kind="requestStatus" />
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
    .batches { margin-bottom: 16px; }
    .item.batch { color: inherit; text-decoration: none; border-top: 1px solid var(--border); }
    .item.batch:hover { background: var(--surface-sunken); }
    .item.batch:last-child { border-bottom: none; }
    .item {
      display: flex; align-items: center; flex-wrap: wrap; gap: 14px;
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
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly realtime = inject(RealtimeService);

  readonly batches = signal<RequestBatchSummaryDto[]>([]);
  readonly loading = signal(true);
  readonly page = signal<PagedResult<RequestSummaryDto>>(
    { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 });

  ngOnInit(): void {
   this.load();
    // Re-fetch on the server's say-so; see syncOn for why it debounces and tears down.
    syncOn(
      [this.realtime.requestChanged],
      () => this.load(),
      this.destroyRef);
  }

  load(): void {
    // Its own call rather than a field on the request rows: a batch is a different shape and
    // a different decision, and folding it into the request query would make both worse.
    this.api.batchReviewQueue().subscribe({
      next: (p) => this.batches.set(p.items),
      error: () => this.batches.set([]),
    });

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
