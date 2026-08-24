import { DestroyRef, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RealtimeService } from '../../core/realtime.service';
import { syncOn } from '../../core/realtime-sync';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { Perm } from '../../core/permissions';
import { DurationPipe } from '../../core/format';
import {
  AttentionItemDto, CoordinatorDashboardDto, HomeDashboardDto, ManagementDashboardDto,
  RequesterDashboardDto, WorkerDashboardDto,
} from '../../core/models';
import { sinceLabel } from '../../core/format';
import { ChipComponent, EmptyComponent, PageHeaderComponent, StatComponent } from '../../shared/ui';

/**
 * One route, four dashboards. Which you get is decided by what you can do, not by a role name —
 * someone who both works tasks and coordinates sees both panels, which a role-keyed switch could
 * not express.
 */
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    RouterLink, DatePipe, MatButtonModule, MatIconModule, MatTabsModule,
    PageHeaderComponent, StatComponent, ChipComponent, EmptyComponent, DurationPipe,
  ],
  template: `
    <div class="page">
      <app-page-header [title]="greeting()" [subtitle]="today">
        @if (auth.has(Perm.requestCreate)) {
          <a matButton="filled" routerLink="/requests/new">
            <mat-icon>add</mat-icon> New request
          </a>
        }
      </app-page-header>

      <!-- --- needs attention / recent activity ---------------------------------------------
           The lead. Everything below this point is context; these two lists are the reason
           someone opens the page. Kept apart because "you must do this" and "this happened"
           are answers to different questions, and merging them makes the reader sort them. -->
      @if (home(); as h) {
        <div class="two-col lead">
          <div class="card">
            <div class="card-pad row">
              <h2 class="card-title" style="margin:0">Needs your attention</h2>
              @if (h.totalNeedingAttention > h.needsAttention.length) {
                <span class="chip tone-neutral">{{ h.totalNeedingAttention }}</span>
              }
            </div>

            @if (h.needsAttention.length === 0) {
              <app-empty message="Nothing is waiting on you" icon="task_alt"
                         hint="Anything that needs a decision or a pair of hands will appear here." />
            } @else {
              @for (item of h.needsAttention; track item.subject + '-' + item.id) {
                <a class="item attention" [routerLink]="routeFor(item)">
                  <span class="reason-dot" [class.urgent]="item.isOverdue || item.priority === 'Critical'"></span>
                  <div class="grow">
                    <div class="row-1">
                      <span class="mono muted small">{{ item.number }}</span>
                      <span class="truncate">{{ item.title }}</span>
                    </div>
                    <div class="muted small">
                      {{ item.reason }} · {{ waiting(item) }}
                    </div>
                  </div>
                  @if (item.isOverdue) { <span class="chip tone-danger nowrap">Overdue</span> }
                  <app-chip [value]="item.priority" kind="priority" />
                </a>
              }
              @if (h.totalNeedingAttention > h.needsAttention.length) {
                <div class="item muted small">
                  and {{ h.totalNeedingAttention - h.needsAttention.length }} more
                </div>
              }
            }
          </div>

          <div class="card">
            <div class="card-pad row">
              <h2 class="card-title" style="margin:0">Recent activity</h2>
            </div>
            @if (h.recentActivity.length === 0) {
              <app-empty message="Nothing has happened yet" icon="history"
                         hint="Updates to work you are part of show up here." />
            } @else {
              @for (entry of h.recentActivity; track entry.subject + '-' + entry.id + '-' + entry.at) {
                <a class="item activity" [routerLink]="routeFor(entry)">
                  <span class="mono muted small">{{ entry.number }}</span>
                  <span class="truncate grow">{{ entry.text }}</span>
                  <span class="muted small nowrap">{{ ago(entry.at) }}</span>
                </a>
              }
            }
          </div>
        </div>
      }

      <!-- --- worker ------------------------------------------------------------------------ -->
      @if (worker(); as w) {
        <section class="stack">
          @if (w.activeTaskNumber) {
            <a class="card running-banner" [routerLink]="['/tasks', w.activeTaskId]">
              <mat-icon>bolt</mat-icon>
              <div>
                <strong>Currently working on {{ w.activeTaskNumber }}</strong>
                <div class="muted small">The timer is running. Open the task to pause or finish.</div>
              </div>
              <span class="spacer"></span>
              <mat-icon>chevron_right</mat-icon>
            </a>
          } @else if (!w.isOnShift && auth.tracksShift()) {
            <div class="card off-shift">
              <mat-icon>schedule</mat-icon>
              <div>
                <strong>You are not on shift</strong>
                <div class="muted small">
                  Start your shift from the top bar before picking up work.
                </div>
              </div>
            </div>
          }

          <div class="stats">
            <app-stat label="In my queue" [value]="w.queueLength" />
            <app-stat label="In progress" [value]="w.inProgressCount" />
            <app-stat label="Cannot continue" [value]="w.blockedCount" [accent]="w.blockedCount > 0" />
            <app-stat label="Needs rework" [value]="w.reworkCount" [accent]="w.reworkCount > 0" />
            <app-stat label="Overdue" [value]="w.overdueCount" [accent]="w.overdueCount > 0" />
            <app-stat label="Worked today" [value]="(w.workedToday | duration)" />
          </div>

        </section>
      }

      <!-- --- coordinator -------------------------------------------------------------------- -->
      @if (coordinator(); as c) {
        <section class="stack section-gap">
          <h2 class="section-title">Coordination</h2>
          <p class="section-note muted small">
            The shape of the floor. Anything needing you personally is in the list above.
          </p>

          <div class="stats">
            <app-stat label="Awaiting review" [value]="c.awaitingReviewCount" />
            <app-stat label="Waiting to be given out" [value]="c.unassignedCount" [accent]="c.unassignedCount > 0" />
            <app-stat label="Cannot continue" [value]="c.blockedCount" [accent]="c.blockedCount > 0" />
            <app-stat label="Awaiting QC" [value]="c.awaitingQCCount" />
            <app-stat label="Overdue" [value]="c.overdueCount" [accent]="c.overdueCount > 0" />
            <app-stat label="On shift" [value]="c.peopleOnShift"
                      [hint]="c.peopleWorking + ' working now'" />
          </div>

          <div class="two-col">
            <div class="card">
              <div class="card-pad row">
                <h2 class="card-title" style="margin:0">Waiting for an assignee</h2>
                <span class="spacer"></span>
                <a matButton routerLink="/assignment">Assign</a>
              </div>
              @if (c.unassigned.length === 0) {
                <app-empty message="Everything is assigned" icon="done_all"
                           hint="Approved work with nobody on it would appear here." />
              } @else {
                @for (item of c.unassigned; track item.id) {
                  <a class="item" [routerLink]="['/tasks', item.id]">
                    <span class="mono muted small">{{ item.number }}</span>
                    <span class="truncate">{{ item.title }}</span>
                    <span class="spacer"></span>
                    <app-chip [value]="item.priority" kind="priority" />
                  </a>
                }
              }
            </div>

            <div class="card">
              <div class="card-pad"><h2 class="card-title" style="margin:0">Overdue</h2></div>
              @if (c.overdue.length === 0) {
                <app-empty message="Nothing is late" icon="schedule"
                           hint="Work past its due date would appear here." />
              } @else {
                @for (item of c.overdue; track item.id) {
                  <a class="item" [routerLink]="['/tasks', item.id]">
                    <span class="mono muted small">{{ item.number }}</span>
                    <span class="truncate">{{ item.title }}</span>
                    <span class="spacer"></span>
                    <span class="overdue small nowrap">{{ item.dueDate | date: 'MMM d' }}</span>
                  </a>
                }
              }
            </div>
          </div>
        </section>
      }

      <!-- --- management --------------------------------------------------------------------- -->
      @if (management(); as m) {
        <section class="stack section-gap">
          <h2 class="section-title">Last 30 days</h2>

          <div class="stats">
            <app-stat label="Requests raised" [value]="m.requestsRaised" />
            <app-stat label="Tasks created" [value]="m.tasksCreated" />
            <app-stat label="Tasks closed" [value]="m.tasksClosed" />
            <app-stat label="QC pass rate" [value]="percent(m.qcPassRate)"
                      [hint]="m.qcAttempts + ' attempts, ' + m.qcFailures + ' failed'" />
            <app-stat label="Avg cycle time"
                      [value]="m.averageCycleTimeHours === null ? '—' : m.averageCycleTimeHours + 'h'" />
            <app-stat label="Hours worked" [value]="m.totalHoursWorked" />
          </div>

          <div class="two-col">
            <div class="card card-pad">
              <h2 class="card-title">Open work by status</h2>
              @for (row of m.openByStatus; track row.label) {
                <div class="bar-row">
                  <span class="bar-label truncate">{{ row.label }}</span>
                  <span class="bar"><i [style.width.%]="width(row.count, m.openByStatus)"></i></span>
                  <span class="mono small">{{ row.count }}</span>
                </div>
              } @empty {
                <p class="muted small">No open work.</p>
              }
            </div>

            <div class="card card-pad">
              <h2 class="card-title">Closed by assignee</h2>
              @for (row of m.closedByAssignee; track row.label) {
                <div class="bar-row">
                  <span class="bar-label truncate">{{ row.label }}</span>
                  <span class="bar"><i [style.width.%]="width(row.count, m.closedByAssignee)"></i></span>
                  <span class="mono small">{{ row.count }}</span>
                </div>
              } @empty {
                <p class="muted small">Nothing closed in this window.</p>
              }
            </div>
          </div>
        </section>
      }

      <!-- --- requester ---------------------------------------------------------------------- -->
      @if (requester(); as r) {
        <section class="stack section-gap">
          <h2 class="section-title">My requests</h2>
          <p class="section-note muted small">Where everything you have asked for got to.</p>

          <div class="stats">
            <app-stat label="Submitted" [value]="r.submittedCount" />
            <app-stat label="Under review" [value]="r.underReviewCount" />
            <app-stat label="Needs my answer" [value]="r.awaitingMyClarificationCount"
                      [accent]="r.awaitingMyClarificationCount > 0" />
            <app-stat label="Being worked" [value]="r.inProgressCount" />
            <app-stat label="Completed" [value]="r.closedCount" />
            <app-stat label="Not proceeding" [value]="r.rejectedCount" />
          </div>

          <div class="card">
            <div class="card-pad row">
              <h2 class="card-title" style="margin:0">Recent</h2>
              <span class="spacer"></span>
              <a matButton routerLink="/requests">All requests</a>
            </div>
            @if (r.recent.length === 0) {
              <app-empty message="You have not raised anything yet" icon="inbox"
                         hint="Anything you ask for shows up here, with its progress."
                         actionLabel="Raise a request" actionRoute="/requests/new" />
            } @else {
              @for (item of r.recent; track item.id) {
                <a class="item" [routerLink]="['/requests', item.id]">
                  <span class="mono muted small">{{ item.number }}</span>
                  <span class="truncate">{{ item.title }}</span>
                  <span class="spacer"></span>
                  <app-chip [value]="item.status" kind="requestStatus" />
                </a>
              }
            }
          </div>
        </section>
      }
    </div>
  `,
  styles: `
    .stats {
      display: grid; gap: 14px;
      grid-template-columns: repeat(auto-fit, minmax(158px, 1fr));
    }
    .two-col { display: grid; gap: 16px; grid-template-columns: repeat(auto-fit, minmax(330px, 1fr)); }
    .section-gap { margin-top: 32px; }
    .section-title {
      font-size: 15px; font-weight: 600; margin: 0; letter-spacing: -0.01em;
    }
    .item {
      display: flex; align-items: center; gap: 12px; flex-wrap: wrap;
      padding: 11px 20px; border-top: 1px solid var(--border);
      color: inherit; text-decoration: none; font-size: 13.5px;
    }
    .item:hover { background: var(--surface-sunken); }
    .lead .card { display: flex; flex-direction: column; }
    .item.attention { align-items: flex-start; padding: 12px 20px; }
    .item .grow { flex: 1 1 auto; min-width: 0; }
    .item .row-1 { display: flex; align-items: baseline; gap: 10px; min-width: 0; }
    .reason-dot {
      flex: 0 0 auto; width: 7px; height: 7px; border-radius: 50%; margin-top: 6px;
      background: var(--text-muted);
    }
    .reason-dot.urgent { background: var(--tone-danger-fg); }
    .section-note { margin: 2px 0 0; }
    .item .mono { flex: 0 0 auto; }
    .running-banner, .off-shift {
      display: flex; align-items: center; gap: 14px; padding: 16px 20px;
      color: inherit; text-decoration: none; flex-wrap: wrap;
    }
    .running-banner { border-left: 3px solid var(--tone-running-fg); }
    .running-banner mat-icon { color: var(--tone-running-fg); }
    .off-shift { border-left: 3px solid var(--tone-warn-fg); }
    .off-shift mat-icon { color: var(--tone-warn-fg); }
    .bar-row { display: flex; align-items: center; gap: 10px; padding: 4px 0; }
    .bar-label { flex: 0 1 130px; min-width: 88px; font-size: 13px; }
    .bar { flex: 1 1 auto; height: 8px; border-radius: 999px; background: var(--surface-sunken); }
    .bar i { display: block; height: 100%; border-radius: 999px; background: #1d69d4; }
  `,
})
export class DashboardComponent implements OnInit {
  private readonly api = inject(ApiService);
  readonly auth = inject(AuthService);
  private readonly realtime = inject(RealtimeService);
  private readonly destroyRef = inject(DestroyRef);
  readonly Perm = Perm;

  readonly home = signal<HomeDashboardDto | null>(null);
  readonly worker = signal<WorkerDashboardDto | null>(null);
  readonly coordinator = signal<CoordinatorDashboardDto | null>(null);
  readonly management = signal<ManagementDashboardDto | null>(null);
  readonly requester = signal<RequesterDashboardDto | null>(null);

  readonly today = new Date().toLocaleDateString(undefined, {
    weekday: 'long', day: 'numeric', month: 'long',
  });

  readonly greeting = computed(() => {
    const hour = new Date().getHours();
    const part = hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';
    const name = this.auth.displayName().split(/[\s(]/)[0];
    return name ? `${part}, ${name}` : part;
  });

  ngOnInit(): void {
    this.load();

    // The dashboard is the screen most likely to be left open on a second monitor, and it was the
    // one screen subscribing to nothing: counts and shift status stayed as they were at sign-in.
    //
    // Every event that can move a number on this page is merged into one stream and debounced, so
    // approving a request that also creates and assigns a task refreshes once rather than three
    // times. takeUntilDestroyed matters as much as the subscription itself: these are long-lived
    // root-scoped subjects, so without teardown each visit to the dashboard would add another
    // listener and multiply the reloads.
    syncOn(
      [this.realtime.taskChanged, this.realtime.requestChanged, this.realtime.workforceChanged],
      () => this.load(),
      this.destroyRef);
  }

  /** Reloads only the panels this user can actually see. */
  private load(): void {
    // Not gated on a permission: everyone has a home screen, and the server decides what goes in
    // it from the caller's own token. A client-side gate here would only be a second, wronger copy.
    this.api.homeDashboard().subscribe((d) => this.home.set(d));

    if (this.auth.has(Perm.taskWork)) {
      this.api.workerDashboard().subscribe((d) => this.worker.set(d));
    }
    if (this.auth.has(Perm.taskAssign)) {
      this.api.coordinatorDashboard().subscribe((d) => this.coordinator.set(d));
    }
    if (this.auth.has(Perm.dashboardManagement)) {
      this.api.managementDashboard().subscribe((d) => this.management.set(d));
    }
    if (this.auth.has(Perm.requestCreate)) {
      this.api.requesterDashboard().subscribe((d) => this.requester.set(d));
    }
  }

  /** Links are built from the subject rather than guessed from the number's prefix. */
  routeFor(item: { subject: string; id: number }): unknown[] {
    return item.subject === 'Request' ? ['/requests', item.id] : ['/tasks', item.id];
  }

  /** "waiting 3 days" — the reason a row is worth acting on now rather than eventually. */
  waiting = (item: AttentionItemDto) => `waiting ${sinceLabel(item.since)}`;

  ago = (at: string) => sinceLabel(at) + ' ago';

  percent = (value: number) => `${Math.round(value * 100)}%`;

  /** Bars are scaled to the biggest row, so a chart of small numbers is still readable. */
  width(count: number, rows: { count: number }[]): number {
    const max = Math.max(...rows.map((r) => r.count), 1);
    return Math.max(3, (count / max) * 100);
  }
}
