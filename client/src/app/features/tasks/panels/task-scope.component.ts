import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ApiService } from '../../../core/api.service';
import { AuthService } from '../../../core/auth.service';
import { ToastService } from '../../../core/toast.service';
import { Perm } from '../../../core/permissions';
import { ScopeChangeDto } from '../../../core/models';
import { EmptyComponent } from '../../../shared/ui';

/**
 * Scope changes are recorded when asked for and only applied when approved. That gap is the point:
 * it keeps a bad estimate distinguishable from work that genuinely grew.
 */
@Component({
  selector: 'app-task-scope',
  standalone: true,
  imports: [
    DatePipe, FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule,
    EmptyComponent,
  ],
  template: `
    <div class="stack">
      <div class="card card-pad">
        <h2 class="card-title">Request a scope change</h2>
        <p class="muted small note">
          The estimate and due date only move once someone with approval rights accepts this.
        </p>

        <mat-form-field class="full">
          <mat-label>What is changing?</mat-label>
          <textarea matInput rows="2" [(ngModel)]="description"></textarea>
        </mat-form-field>

        <div class="row row-wrap">
          <mat-form-field class="grow">
            <mat-label>Why (optional)</mat-label>
            <input matInput [(ngModel)]="reason" />
          </mat-form-field>
          <mat-form-field class="hours">
            <mat-label>Impact (h)</mat-label>
            <input matInput type="number" [(ngModel)]="impact" />
          </mat-form-field>
          <span class="spacer"></span>
          <button matButton="filled" [disabled]="!description.trim() || busy()" (click)="request()">
            Record
          </button>
        </div>
      </div>

      <div class="card">
        <div class="card-pad"><h2 class="card-title" style="margin:0">History</h2></div>
        @if (changes().length === 0) {
          <app-empty message="The scope has not changed" icon="rule"
                     hint="Record a change when the job turns out bigger or smaller than agreed — the original estimate is kept either way." />
        } @else {
          @for (change of changes(); track change.id) {
            <div class="change">
              <div class="row">
                <strong class="small">{{ change.requestedByDisplayName }}</strong>
                <span class="muted small">{{ change.requestedAt | date: 'MMM d, HH:mm' }}</span>
                <span class="spacer"></span>
                @if (change.approvedAt) {
                  <span class="chip tone-good">Approved</span>
                } @else {
                  <span class="chip tone-warn">Pending</span>
                  @if (canApprove) {
                    <button matButton="filled" (click)="approve(change)">Approve</button>
                  }
                }
              </div>
              <p class="desc">{{ change.description }}</p>
              <div class="muted small">
                @if (change.reason) { {{ change.reason }} · }
                @if (change.estimatedImpactHours != null) { {{ change.estimatedImpactHours }}h impact }
              </div>
            </div>
          }
        }
      </div>
    </div>
  `,
  styles: `
    .full { width: 100%; }
    .grow { flex: 1 1 200px; margin-bottom: -1.25em; }
    .hours { width: 130px; margin-bottom: -1.25em; }
    .note { margin: -6px 0 12px; }
    .change { padding: 13px 20px; border-top: 1px solid var(--border); }
    .change:first-of-type { border-top: none; }
    .desc { margin: 7px 0 3px; font-size: 13.5px; white-space: pre-wrap; }
  `,
})
export class TaskScopeComponent implements OnInit {
  readonly taskId = input.required<number>();
  readonly changed = output<void>();

  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  readonly changes = signal<ScopeChangeDto[]>([]);
  readonly busy = signal(false);
  readonly canApprove = this.auth.has(Perm.taskApprove);

  description = '';
  reason = '';
  impact: number | null = null;

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.scopeChanges(this.taskId()).subscribe((list) => this.changes.set(list));
  }

  request(): void {
    this.busy.set(true);
    this.api.requestScopeChange(this.taskId(), {
      description: this.description.trim(),
      reason: this.reason.trim() || undefined,
      estimatedImpactHours: this.impact ?? undefined,
    }).subscribe({
      next: () => {
        this.busy.set(false);
        this.description = '';
        this.reason = '';
        this.impact = null;
        this.toast.success('Scope change recorded.');
        this.load();
      },
      error: () => this.busy.set(false),
    });
  }

  approve(change: ScopeChangeDto): void {
    this.api.approveScopeChange(change.id).subscribe(() => {
      this.toast.success('Scope change approved — the estimate has been updated.');
      this.load();
      this.changed.emit();
    });
  }
}
