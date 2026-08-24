import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpContext } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { Perm } from '../../core/permissions';
import { ToastService } from '../../core/toast.service';
import { FormSubmit } from '../../core/form-submit';
import { syncOn } from '../../core/realtime-sync';
import { RealtimeService } from '../../core/realtime.service';
import { BatchItemSummaryDto, Priority, RequestBatchDetailDto } from '../../core/models';
import { requestTypeLabel, urgencyLabel } from '../../core/labels';
import { enumOptions, SearchSelectComponent } from '../../shared/search-select.component';
import { AttachmentsComponent } from '../../shared/attachments.component';
import { BreadcrumbsComponent, Crumb } from '../../shared/breadcrumbs.component';
import { ChipComponent, EmptyComponent, FieldComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';

/** Settling the terms of the combined task before it exists. */
@Component({
  selector: 'app-fold-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, SearchSelectComponent,
  ],
  template: `
    <h2 mat-dialog-title>
      {{ data.items.length === 1 ? 'Approve this item' : 'Approve ' + data.items.length + ' items as one task' }}
    </h2>
    <mat-dialog-content>
      <ul class="chosen">
        @for (item of data.items; track item.id) {
          <li><span class="mono muted small">{{ item.requestNumber }}</span> {{ item.title }}</li>
        }
      </ul>

      @if (data.items.length > 1) {
        <p class="muted small">
          They stay {{ data.items.length }} separate requests — each is approved in its own right and
          each requester still sees their own. They simply share one piece of work.
        </p>
      }

      <mat-form-field appearance="outline" class="full">
        <mat-label>Title for the work</mat-label>
        <input matInput name="taskTitle" [(ngModel)]="taskTitle" maxlength="300" />
      </mat-form-field>

      <app-search-select class="full" label="Priority" [options]="priorityOptions"
                         name="priority" [(ngModel)]="priority" />

      <mat-form-field appearance="outline" class="full">
        <mat-label>Estimate (hours)</mat-label>
        <input matInput type="number" name="estimate" [(ngModel)]="estimate" min="0" />
      </mat-form-field>

      <mat-form-field appearance="outline" class="full">
        <mat-label>Acceptance criteria — one per line</mat-label>
        <textarea matInput rows="3" name="criteria" [(ngModel)]="criteria" maxlength="4000"></textarea>
      </mat-form-field>

      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" (click)="submit()" [disabled]="!taskTitle.trim() || form.busy()">
        Approve and create the work
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content { min-width: min(520px, 86vw); }
    .full { width: 100%; }
    .chosen { margin: 0 0 12px; padding-left: 18px; font-size: 13.5px; }
    .chosen li { margin-bottom: 3px; }
    .form-error { display: flex; gap: 7px; align-items: flex-start; color: var(--tone-danger-fg); }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class FoldDialog {
  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<FoldDialog>);
  readonly data = inject<{ batchId: number; batchTitle: string; items: BatchItemSummaryDto[] }>(
    MAT_DIALOG_DATA);
  readonly form = new FormSubmit();

  readonly priorities: Priority[] = ['Critical', 'High', 'Normal', 'Low'];
  readonly priorityOptions = enumOptions(this.priorities);

  taskTitle = this.data.items.length === 1 ? this.data.items[0].title : this.data.batchTitle;
  priority: Priority = 'Normal';
  estimate: number | null = null;
  criteria = '';

  submit(): void {
    if (!this.taskTitle.trim()) return;

    this.ref.disableClose = true;
    this.form.run(
      (ctx: HttpContext) => this.api.approveTogether(this.data.batchId, {
        requestIds: this.data.items.map((i) => i.id),
        taskTitle: this.taskTitle.trim(),
        approvedPriority: this.priority,
        estimatedEffortHours: this.estimate ?? null,
        acceptanceCriteria: this.criteria.trim() || null,
      }, ctx),
      (result) => { this.ref.disableClose = false; this.ref.close(result); },
    );
  }
}

/**
 * One submission, several things asked for.
 *
 * The screen exists for one decision a reviewer cannot make anywhere else: **which of these are the
 * same piece of work.** Everything else about an item — asking a question, rejecting it, editing
 * it — happens on the item's own page, because a batch item is an ordinary request and duplicating
 * its screen here would be two places to keep in step.
 *
 * So the items are shown with checkboxes and one action. Items already decided are listed but not
 * selectable: a reviewer who has already dealt with something should see that they have, not be
 * offered it again.
 */
@Component({
  selector: 'app-batch-detail',
  standalone: true,
  imports: [
    DatePipe, RouterLink, MatButtonModule, MatCheckboxModule, MatIconModule,
    PageHeaderComponent, LoadingComponent, EmptyComponent, FieldComponent, ChipComponent,
    AttachmentsComponent, BreadcrumbsComponent,
  ],
  template: `
    <div class="page">
      @if (loading()) {
        <app-loading />
      } @else if (batch(); as b) {
        <app-breadcrumbs [crumbs]="crumbs(b)" />

        <app-page-header [title]="b.title" [subtitle]="b.batchNumber + ' · ' + b.items.length + ' requests'">
          @if (canApprove() && selected().length > 0) {
            <button matButton="filled" (click)="fold()">
              <mat-icon>merge</mat-icon>
              {{ selected().length === 1
                  ? 'Approve this item'
                  : 'Approve ' + selected().length + ' as one task' }}
            </button>
          }
        </app-page-header>

        <div class="layout">
          <div class="stack">
            <div class="card">
              <div class="card-pad row">
                <h2 class="card-title" style="margin:0">What was asked for</h2>
                <span class="spacer"></span>
                @if (canApprove() && selectable().length > 1) {
                  <button matButton (click)="toggleAll()">
                    {{ allSelected() ? 'Clear' : 'Select all waiting' }}
                  </button>
                }
              </div>

              @for (item of b.items; track item.id) {
                <div class="item" [class.decided]="!isSelectable(item)">
                  @if (canApprove()) {
                    <mat-checkbox [checked]="isSelected(item)" [disabled]="!isSelectable(item)"
                                  (change)="toggle(item)"
                                  [attr.aria-label]="'Select ' + item.requestNumber" />
                  } @else {
                    <span class="ordinal muted small mono">{{ item.ordinal }}</span>
                  }

                  <a class="body" [routerLink]="['/requests', item.id]">
                    <div class="line1">
                      <span class="mono muted small">{{ item.requestNumber }}</span>
                      <strong class="truncate">{{ item.title }}</strong>
                    </div>
                    <div class="muted small line2">
                      {{ type(item.type) }} · {{ urgency(item.requestedUrgency) }}
                      @if (item.generatedTaskNumber) {
                        · became {{ item.generatedTaskNumber }}
                      }
                      @if (item.sharedTaskWith.length) {
                        · with {{ item.sharedTaskWith.join(', ') }}
                      }
                    </div>
                  </a>

                  <span class="spacer"></span>
                  <app-chip [value]="item.status" kind="requestStatus" />
                </div>
              }
            </div>

            @if (b.attachments.length) {
              <div class="card card-pad">
                <h2 class="card-title">Files</h2>
                <p class="muted small">Shared by every item in this submission.</p>
                <app-attachments [attachments]="b.attachments" />
              </div>
            }
          </div>

          <aside class="side stack">
            <div class="card card-pad">
              <h2 class="card-title">Details</h2>
              <app-field label="Raised by">{{ b.requestedByDisplayName }}</app-field>
              <app-field label="Raised on">{{ b.requestedAt | date: 'medium' }}</app-field>
              @if (b.clientName) { <app-field label="Client">{{ b.clientName }}</app-field> }
              <app-field label="Still waiting">
                {{ waiting(b) }} of {{ b.items.length }}
              </app-field>
            </div>

            @if (b.note) {
              <div class="card card-pad">
                <h2 class="card-title">Note</h2>
                <p class="body-text">{{ b.note }}</p>
              </div>
            }
          </aside>
        </div>
      } @else {
        <app-empty message="That submission is not here" icon="inbox"
                   hint="It may have been removed, or belong to someone else."
                   actionLabel="All requests" actionRoute="/requests" />
      }
    </div>
  `,
  styles: `
    .layout { display: grid; gap: 16px; grid-template-columns: minmax(0, 1fr) 300px; }
    @media (max-width: 1150px) { .layout { grid-template-columns: 1fr; } }
    .item {
      display: flex; align-items: center; gap: 12px; flex-wrap: wrap;
      padding: 11px 20px; border-top: 1px solid var(--border);
    }
    .item.decided { opacity: 0.72; }
    .item .body {
      flex: 1 1 260px; min-width: 0; color: inherit; text-decoration: none;
    }
    .item .body:hover .line1 strong { text-decoration: underline; }
    .line1 { display: flex; align-items: baseline; gap: 9px; min-width: 0; }
    .line2 { margin-top: 1px; }
    .ordinal { width: 18px; flex: 0 0 auto; }
  `,
})
export class BatchDetailComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);

  /** Bound from the route by withComponentInputBinding(). */
  readonly id = input.required<string>();

  readonly batch = signal<RequestBatchDetailDto | null>(null);
  readonly loading = signal(true);
  readonly chosen = signal<Set<number>>(new Set());

  readonly canApprove = computed(() => this.auth.has(Perm.taskApprove));

  type = (value: string) => requestTypeLabel(value as never);
  urgency = (value: string) => urgencyLabel(value as never);

  /** Items still awaiting a decision — the only ones that can be folded. */
  readonly selectable = computed(() =>
    (this.batch()?.items ?? []).filter((i) => this.isSelectable(i)));

  readonly selected = computed(() =>
    this.selectable().filter((i) => this.chosen().has(i.id)));

  readonly allSelected = computed(() =>
    this.selectable().length > 0 && this.selected().length === this.selectable().length);

  isSelectable = (item: BatchItemSummaryDto) =>
    item.status === 'Submitted' || item.status === 'InReview';

  isSelected = (item: BatchItemSummaryDto) => this.chosen().has(item.id);

  ngOnInit(): void {
    this.load();

    // Re-fetch rather than patch, like everywhere else: another reviewer may be working through
    // the same submission.
    syncOn([this.realtime.requestChanged, this.realtime.taskChanged], () => this.load(false),
      this.destroyRef);
  }

  crumbs(b: RequestBatchDetailDto): Crumb[] {
    return [{ label: 'Requests', route: '/requests' }, { label: b.batchNumber }];
  }

  waiting = (b: RequestBatchDetailDto) =>
    b.items.filter((i) => this.isSelectable(i)).length;

  toggle(item: BatchItemSummaryDto): void {
    this.chosen.update((set) => {
      const next = new Set(set);
      if (next.has(item.id)) next.delete(item.id);
      else next.add(item.id);
      return next;
    });
  }

  toggleAll(): void {
    this.chosen.set(this.allSelected()
      ? new Set()
      : new Set(this.selectable().map((i) => i.id)));
  }

  fold(): void {
    const b = this.batch();
    const items = this.selected();
    if (!b || items.length === 0) return;

    this.dialog
      .open(FoldDialog, { data: { batchId: b.id, batchTitle: b.title, items } })
      .afterClosed()
      .subscribe((result?: { createdTaskId: number; createdTaskNumber: string }) => {
        if (!result) return;

        this.chosen.set(new Set());
        this.toast.success(
          items.length === 1
            ? `Approved as ${result.createdTaskNumber}.`
            : `${items.length} requests approved as ${result.createdTaskNumber}.`);

        void this.router.navigate(['/tasks', result.createdTaskId]);
      });
  }

  private load(showSpinner = true): void {
    if (showSpinner) this.loading.set(true);

    this.api.batch(Number(this.id())).subscribe({
      next: (b) => { this.batch.set(b); this.loading.set(false); },
      error: () => { this.batch.set(null); this.loading.set(false); },
    });
  }
}
