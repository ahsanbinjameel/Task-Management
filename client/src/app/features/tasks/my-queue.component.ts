import { DestroyRef, Component, OnInit, inject, signal } from '@angular/core';
import { syncOn } from '../../core/realtime-sync';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { RealtimeService } from '../../core/realtime.service';
import { TaskSummaryDto } from '../../core/models';
import { DurationPipe } from '../../core/format';
import { ChipComponent, EmptyComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';

/**
 * The worker's own ordered queue.
 *
 * Drag to reorder: the API lets an assignee sequence their own work, and priority only breaks ties.
 * The new order is saved on drop, and the list is reverted if the save fails — an order that looks
 * saved but is not would send someone to the wrong task tomorrow morning.
 */
@Component({
  selector: 'app-my-queue',
  standalone: true,
  imports: [
    DragDropModule, MatButtonModule, MatIconModule, MatTooltipModule,
    PageHeaderComponent, EmptyComponent, LoadingComponent, ChipComponent, DurationPipe,
    DatePipe,
  ],
  template: `
    <div class="page">
      <app-page-header title="My queue" subtitle="Work through these in order. Drag to change the order.">
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon> Refresh</button>
      </app-page-header>

      @if (loading()) {
        <app-loading />
      } @else if (tasks().length === 0) {
        <div class="card">
          <app-empty message="Nothing assigned to you" icon="task_alt"
                     hint="Work appears here once a coordinator assigns it to you." />
        </div>
      } @else {
        <p class="muted small lead">
          Start at the top. The rest are queued behind it — drag one up if it should come first.
        </p>

        <div class="card" cdkDropList (cdkDropListDropped)="drop($event)">
          @for (task of tasks(); track task.id; let i = $index) {
            <div class="item" cdkDrag [class.later]="!isNext(task, i)">
              <mat-icon class="handle" cdkDragHandle matTooltip="Drag to reorder">drag_indicator</mat-icon>

              <div class="body" (click)="open(task)">
                <div class="line1">
                  <span class="mono muted small">{{ task.taskNumber }}</span>
                  <strong class="truncate">{{ task.title }}</strong>
                  @if (task.hasActiveSession) {
                    <mat-icon class="running" matTooltip="Timer running">bolt</mat-icon>
                  }
                </div>
                <div class="line2">
                  <app-chip [value]="task.status" kind="status" />
                  <app-chip [value]="task.priority" kind="priority" />
                  @if (task.dueDate) {
                    <span class="small" [class.overdue]="overdue(task)">
                      Due {{ task.dueDate | date: 'MMM d' }}
                    </span>
                  }
                  <span class="small muted">{{ task.totalWorkedTime | duration }} logged</span>
                </div>
              </div>

              @if (isNext(task, i)) {
                <button matButton="filled" (click)="open(task)">Open</button>
              } @else {
                <span class="waiting small muted">Later</span>
                <button matButton (click)="open(task)">View</button>
              }
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: `
    .lead { margin: 0 0 10px; }
    /* Dimmed, not hidden: you can still see and open what is coming, it is just not the one to
       pick up now. Anything already running stays at full strength wherever it sits. */
    .item.later { opacity: .62; }
    .item.later:hover { opacity: 1; }
    .waiting { margin-right: 8px; }
    .item {
      display: flex; align-items: center; flex-wrap: wrap; gap: 14px;
      padding: 14px 18px; border-bottom: 1px solid var(--border); background: var(--surface);
    }
    .item:last-child { border-bottom: none; }
    .handle { cursor: grab; color: var(--text-muted); }
    .body { flex: 1 1 auto; min-width: 0; cursor: pointer; }
    .line1 { display: flex; align-items: center; gap: 9px; }
    .line2 { display: flex; align-items: center; gap: 9px; margin-top: 5px; flex-wrap: wrap; }
    .running { color: var(--tone-running-fg); font-size: 18px; width: 18px; height: 18px; }
    .cdk-drag-preview { box-shadow: 0 8px 24px rgba(16,24,40,0.18); border-radius: 8px; }
    .cdk-drag-placeholder { opacity: 0.35; }
  `,
})
export class MyQueueComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly realtime = inject(RealtimeService);

  readonly tasks = signal<TaskSummaryDto[]>([]);
  readonly loading = signal(true);

  ngOnInit(): void {
    this.load();
    // Someone assigning me work should make it appear without a refresh.
  
    // Re-fetch on the server's say-so; see syncOn for why it debounces and tears down.
    syncOn(
      [this.realtime.taskChanged],
      () => this.load(),
      this.destroyRef);
  }

  load(): void {
    this.api.myQueue().subscribe({
      next: (tasks) => { this.tasks.set(tasks); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  drop(event: CdkDragDrop<unknown>): void {
    const previous = this.tasks();
    const reordered = [...previous];
    moveItemInArray(reordered, event.previousIndex, event.currentIndex);
    this.tasks.set(reordered);

    this.api.reorderQueue(reordered.map((t) => t.id)).subscribe({
      next: () => this.toast.success('Queue order saved.'),
      // Put it back: a queue that silently did not save is worse than one that refused to move.
      error: () => this.tasks.set(previous),
    });
  }

  overdue(task: TaskSummaryDto): boolean {
    return !!task.dueDate && new Date(task.dueDate) < new Date();
  }

  /**
   * Whether this is the one to pick up now: the top of the queue, or anything already running.
   *
   * A worker with a paused task further down still needs to be able to get back to it, so this
   * dims rather than disables — the aim is to make the next task obvious, not to lock the others
   * away. The single-active-session rule is enforced by the server regardless.
   */
  isNext(task: TaskSummaryDto, index: number): boolean {
    return index === 0 || task.hasActiveSession;
  }

  open(task: TaskSummaryDto): void {
    void this.router.navigate(['/tasks', task.id]);
  }
}
