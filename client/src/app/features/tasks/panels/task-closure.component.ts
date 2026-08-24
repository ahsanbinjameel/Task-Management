import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { ApiService } from '../../../core/api.service';
import { AuthService } from '../../../core/auth.service';
import { ToastService } from '../../../core/toast.service';
import { Perm } from '../../../core/permissions';
import { ClosureChecklistDto, TaskDetailDto } from '../../../core/models';
import { ReasonDialog, ReasonData} from '../../../shared/dialogs';

/**
 * The closure checklist.
 *
 * The server exposes closure preconditions as a named list precisely so a client can show *why*
 * the button is disabled rather than offering it and returning a 409. That is the whole reason this
 * panel exists — an unmet requirement here reads as a to-do, not an error.
 */
@Component({
  selector: 'app-task-closure',
  standalone: true,
  imports: [MatButtonModule, MatIconModule],
  template: `
    @if (visible()) {
      <div class="card card-pad">
        <h2 class="card-title">Closure</h2>

        @if (task().status === 'Closed') {
          <p class="closed small">
            <mat-icon>lock</mat-icon> This task is closed.
          </p>
          @if (auth.has(Perm.taskReopen)) {
            <button matButton class="full" (click)="reopen()">
              <mat-icon>lock_open</mat-icon> Reopen
            </button>
          }
        } @else if (checklist(); as c) {
          @for (requirement of c.requirements; track requirement.code) {
            <div class="req" [class.met]="requirement.isMet">
              <mat-icon>{{ requirement.isMet ? 'check_circle' : 'radio_button_unchecked' }}</mat-icon>
              <div>
                <div class="small">{{ requirement.description }}</div>
                @if (!requirement.isMet && requirement.detail) {
                  <div class="detail small">{{ requirement.detail }}</div>
                }
              </div>
            </div>
          }

          <button matButton="filled" class="full close-btn"
                  [disabled]="!c.isReady || busy()" (click)="close()">
            <mat-icon>task_alt</mat-icon> Close task
          </button>

          @if (!c.isReady) {
            <p class="muted small hint">Everything above has to be ticked first.</p>
          }
        }
      </div>
    }
  `,
  styles: `
    .req { display: flex; gap: 8px; align-items: flex-start; padding: 6px 0; }
    .req mat-icon { font-size: 18px; width: 18px; height: 18px; color: var(--text-muted); flex: 0 0 auto; }
    .req.met mat-icon { color: var(--tone-good-fg); }
    .req.met .small { color: var(--text-muted); }
    .detail { color: var(--tone-danger-fg); margin-top: 1px; }
    .full { width: 100%; }
    .close-btn { margin-top: 14px; --mdc-filled-button-container-color: #17603a; }
    .closed { display: flex; align-items: center; gap: 7px; color: var(--text-muted); margin: 0 0 12px; }
    .closed mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .hint { text-align: center; margin: 8px 0 0; }
  `,
})
export class TaskClosureComponent implements OnInit {
  readonly task = input.required<TaskDetailDto>();
  readonly changed = output<void>();

  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly toast = inject(ToastService);

  readonly Perm = Perm;
  readonly checklist = signal<ClosureChecklistDto | null>(null);
  readonly busy = signal(false);

  /** Only relevant once QC has passed, or once it is closed and might be reopened. */
  readonly visible = computed(() => {
    const status = this.task().status;
    if (status === 'Closed') return this.auth.has(Perm.taskReopen) || true;
    return this.auth.has(Perm.taskClose)
      && ['QCPassed', 'ReadyForClosure'].includes(status);
  });

  ngOnInit(): void {
    if (this.auth.has(Perm.taskClose) && this.task().status !== 'Closed') {
      this.api.closureCheck(this.task().id).subscribe({
        next: (c) => this.checklist.set(c),
        error: () => undefined,
      });
    }
  }

  close(): void {
    this.busy.set(true);
    this.api.closeTask(this.task().id).subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success('Task closed.');
        this.changed.emit();
      },
      error: () => this.busy.set(false),
    });
  }

  reopen(): void {
    this.dialog
      .open<ReasonDialog, ReasonData>(ReasonDialog, {
        data: {
          title: 'Open this task again',
          message: 'It will need to pass a fresh quality check before it can be closed again.',
          label: 'Why does this need more work?',
          confirmText: 'Open again',
          danger: true,
          submit: (reason: string, ctx) => this.api.reopenTask(this.task().id, reason, ctx),
        },
      })
      .afterClosed()
      .subscribe((done?: unknown) => {
        if (!done) return;
        this.toast.success('This task is open again.');
        this.changed.emit();
      });
  }
}
