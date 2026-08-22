import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { RealtimeService } from '../../core/realtime.service';
import { Perm } from '../../core/permissions';
import { ActiveWorkforceDto, ActiveWorkerDto } from '../../core/models';
import { ReasonDialog } from '../../shared/dialogs';
import {
  ChipComponent, EmptyComponent, LoadingComponent, PageHeaderComponent, StatComponent,
} from '../../shared/ui';

@Component({
  selector: 'app-active-workforce',
  standalone: true,
  imports: [
    DatePipe, RouterLink, MatButtonModule, MatIconModule,
    PageHeaderComponent, ChipComponent, EmptyComponent, LoadingComponent, StatComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="Who's working" subtitle="Live view of everyone on shift.">
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon> Refresh</button>
      </app-page-header>

      @if (loading()) {
        <app-loading />
      } @else if (data(); as d) {
        <div class="stats">
          <app-stat label="On shift" [value]="d.totalOnShift" />
          <app-stat label="Working" [value]="d.working" />
          <app-stat label="Available" [value]="d.available" />
          <app-stat label="Away" [value]="d.away" />
        </div>

        <div class="card top-gap">
          @if (d.workers.length === 0) {
            <app-empty message="Nobody is on shift" icon="sensors_off" />
          } @else {
            @for (worker of d.workers; track worker.userId) {
              <div class="worker">
                <div class="who">
                  <strong>{{ worker.displayName }}</strong>
                  <span class="muted small">
                    On since {{ worker.shiftStart | date: 'HH:mm' }}
                  </span>
                </div>

                <app-chip [value]="worker.state" kind="workforce" [dot]="true" />

                @if (worker.activeTaskNumber) {
                  <a class="mono small" [routerLink]="['/tasks', worker.activeTaskId]">
                    {{ worker.activeTaskNumber }}
                  </a>
                } @else {
                  <span class="muted small">No task running</span>
                }

                <span class="spacer"></span>

                @if (canManage) {
                  <button matButton (click)="forceEnd(worker)">End shift</button>
                }
              </div>
            }
          }
        </div>
      }
    </div>
  `,
  styles: `
    .stats { display: grid; gap: 14px; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); }
    .top-gap { margin-top: 18px; }
    .worker {
      display: flex; align-items: center; gap: 14px;
      padding: 12px 18px; border-bottom: 1px solid var(--border);
    }
    .worker:last-child { border-bottom: none; }
    .who { display: flex; flex-direction: column; min-width: 190px; }
  `,
})
export class ActiveWorkforceComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);
  private readonly realtime = inject(RealtimeService);

  readonly data = signal<ActiveWorkforceDto | null>(null);
  readonly loading = signal(true);
  readonly canManage = this.auth.has(Perm.workforceManageOthers);

  ngOnInit(): void {
    this.load();
    // Availability changes are pushed, so this screen stays live without polling.
    this.realtime.workforceChanged.subscribe(() => this.load());
  }

  load(): void {
    this.api.activeWorkforce().subscribe({
      next: (d) => { this.data.set(d); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  forceEnd(worker: ActiveWorkerDto): void {
    this.dialog
      .open(ReasonDialog, {
        data: {
          title: `End ${worker.displayName}'s shift`,
          message: 'This alters their attendance record, so it always carries a reason.',
          label: 'Reason',
          confirmText: 'End shift',
          danger: true,
        },
      })
      .afterClosed()
      .subscribe((reason?: string) => {
        if (!reason) return;

        this.api.forceEndShift(worker.userId, reason).subscribe(() => {
          this.toast.success(`Shift ended for ${worker.displayName}.`);
          this.load();
        });
      });
  }
}
