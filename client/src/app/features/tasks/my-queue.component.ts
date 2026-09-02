import { DestroyRef, Component, OnInit, inject, signal } from '@angular/core';
import { syncOn } from '../../core/realtime-sync';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../../core/api.service';
import { WorkTimerService } from '../../core/work-timer.service';
import { ToastService } from '../../core/toast.service';
import { RealtimeService } from '../../core/realtime.service';
import { TaskSummaryDto } from '../../core/models';
import { DurationPipe } from '../../core/format';
import { ChipComponent, EmptyComponent, LoadingComponent, PageHeaderComponent } from '../../shared/ui';
import { ViewTabsComponent } from '../../shared/view-tabs.component';

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
    DatePipe, ViewTabsComponent,
  ],
  template: `
    <div class="page">
      <app-page-header title="My queue">
        <button matButton (click)="load()"><mat-icon>refresh</mat-icon> Refresh</button>
      </app-page-header>

      <app-view-tabs group="my-tasks" />

      @if (loading()) {
        <app-loading />
      } @else if (tasks().length === 0) {
        <div class="card">
          <app-empty message="Nothing assigned to you" icon="task_alt" />
        </div>
      } @else {
        <div class="card" cdkDropList (cdkDropListDropped)="drop($event)">
          @for (task of tasks(); track task.id; let i = $index) {
            <div class="item" cdkDrag [class.later]="!isNext(task, i)">
              <mat-icon class="handle" cdkDragHandle matTooltip="Drag to reorder">drag_indicator</mat-icon>

              <div class="body" (click)="open(task)">
                <div class="line1">
                  <span class="mono muted small">{{ task.taskNumber }}</span>
                  <strong class="truncate">{{ task.title }}</strong>
                  <!--
                    A bolt said "running"; it could not say "running since when". On the one screen
                    a worker keeps open all day that is the difference between noticing a timer left
                    going over lunch and not. This queue is the caller's own work, so the shared
                    clock is necessarily theirs — no per-row fetch and no second interval.
                  -->
                  @if (timer.isRunning(task.id)) {
                    <span class="running-clock mono" matTooltip="Timer running on this task">
                      <mat-icon>timer</mat-icon>{{ timer.clock() }}
                    </span>
                  } @else if (task.hasActiveSession) {
                    <mat-icon class="running" matTooltip="Timer running">bolt</mat-icon>
                  }
                </div>

                <!--
                  Where in the product this is. First thing a worker asks and, until now, the
                  first thing they had to open the task to find out (PRODUCT-CORE §12A).
                -->
                @if (task.clientName || task.productLocation) {
                  <div class="context small">
                    @if (task.clientName) { <span class="client">{{ task.clientName }}</span> }
                    @if (task.clientName && task.productLocation) { <span class="dot">·</span> }
                    @if (task.productLocation) { <span>{{ task.productLocation }}</span> }
                  </div>
                }

                <!-- What "done" is supposed to look like, in the requester's own words. -->
                @if (task.expectedResult) {
                  <div class="expected small">
                    <mat-icon>flag</mat-icon>
                    <span class="truncate">{{ task.expectedResult }}</span>
                  </div>
                }

                <div class="line2">
                  <app-chip [value]="task.status" kind="status" />
                  <app-chip [value]="task.priority" kind="priority" />
                  @if (task.dueDate) {
                    <span class="small" [class.overdue]="overdue(task)">
                      Due {{ task.dueDate | date: 'MMM d' }}
                    </span>
                  }
                  @if (timer.isRunning(task.id)) {
                    <span class="small muted">{{ timer.totalHuman() }} logged</span>
                  } @else {
                    <span class="small muted">{{ task.totalWorkedTime | duration }} logged</span>
                  }
                  @if (task.attachmentCount > 0) {
                    <span class="small muted attach"
                          [matTooltip]="task.attachmentCount === 1
                                        ? '1 file to look at'
                                        : task.attachmentCount + ' files to look at'">
                      <mat-icon>attach_file</mat-icon>{{ task.attachmentCount }}
                    </span>
                  }
                </div>
              </div>

              <!--
                Start is the whole point of this screen, so it is on the row rather than one page
                further in. It routes through the task detail with ?start=1, which already owns the
                confirmation and the pause-what-was-running sequence — a second implementation of
                starting work is exactly how the two would drift apart.
              -->
              @if (task.hasActiveSession) {
                <button matButton="filled" (click)="open(task)">Open</button>
              } @else if (canStart(task)) {
                <button matButton="filled" (click)="start(task)">
                  <mat-icon>play_arrow</mat-icon> Start
                </button>
              } @else if (isNext(task, i)) {
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
    .context { color: var(--text-muted); margin-top: 3px; }
    .context .client { color: var(--text); font-weight: 500; }
    .context .dot { margin: 0 5px; }
    .expected {
      display: flex; align-items: center; gap: 5px;
      color: var(--text-muted); margin-top: 3px; min-width: 0;
    }
    .expected mat-icon { font-size: 14px; width: 14px; height: 14px; flex: none; }
    .attach { display: inline-flex; align-items: center; gap: 2px; }
    .attach mat-icon { font-size: 15px; width: 15px; height: 15px; }
    .running { color: var(--tone-running-fg); font-size: 18px; width: 18px; height: 18px; }
    .running-clock {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      color: var(--tone-running-fg);
      font-weight: 600;
      font-variant-numeric: tabular-nums;
    }
    .running-clock mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .cdk-drag-preview { box-shadow: 0 8px 24px rgba(16,24,40,0.18); border-radius: 8px; }
    .cdk-drag-placeholder { opacity: 0.35; }
  `,
})
export class MyQueueComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly api = inject(ApiService);
  readonly timer = inject(WorkTimerService);
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

  /**
   * Whether starting is the obvious next move. Deliberately the same set the task detail's own
   * Start button uses — the server is what enforces it, and offering a button that 409s teaches
   * people to distrust the screen.
   *
   * Blocked is absent: a task waiting on unfinished work is refused by `WorkSessionService`, and
   * `Paused` is present because resuming is exactly what a paused task is for.
   */
  canStart(task: TaskSummaryDto): boolean {
    return ['Assigned', 'ReadyToStart', 'Paused', 'QCFailedRework', 'Reopened']
      .includes(task.status);
  }

  /** Hands off to the task detail, which owns the confirmation and the interrupt sequence. */
  start(task: TaskSummaryDto): void {
    void this.router.navigate(['/tasks', task.id], { queryParams: { start: 1 } });
  }

  open(task: TaskSummaryDto): void {
    void this.router.navigate(['/tasks', task.id]);
  }
}
