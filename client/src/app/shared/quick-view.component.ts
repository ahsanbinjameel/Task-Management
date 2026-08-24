import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../core/api.service';
import { RequestDetailDto, TaskDetailDto } from '../core/models';
import { DurationPipe, sinceLabel } from '../core/format';
import { requestTypeLabel } from '../core/labels';
import { ChipComponent, FieldComponent, LoadingComponent } from './ui';

/** What the drawer is looking at. */
export interface QuickViewTarget {
  kind: 'task' | 'request';
  id: number;
}

/**
 * A look at one row without leaving the list.
 *
 * The brief that asked for this warned in the same breath against duplicating the detail page, and
 * that warning is the design. So the drawer is **read-only and deliberately incomplete**: it
 * answers "is this the one I am looking for?" and nothing else. No tabs, no comments, no timer, no
 * quality check, no actions — every one of those would be a second implementation of a screen that
 * already exists and would drift from it within a month.
 *
 * <p>
 * It also holds no logic of its own. It fetches the same detail the full page fetches and renders a
 * handful of fields from it; anything the reader wants beyond that is one click away on the page
 * itself, which is the point.
 * </p>
 *
 * <p>
 * Desktop only. On a narrow screen there is no room for a panel beside a list, and the full page is
 * the better answer anyway — so below 1100px the trigger is hidden and the row simply navigates.
 * </p>
 */
@Component({
  selector: 'app-quick-view',
  standalone: true,
  imports: [
    DatePipe, RouterLink, MatButtonModule, MatIconModule,
    ChipComponent, FieldComponent, LoadingComponent, DurationPipe,
  ],
  template: `
    @if (target(); as t) {
      <div class="qv-scrim" (click)="close.emit()"></div>

      <aside class="drawer" role="dialog" aria-label="Quick view">
        <header class="head">
          <span class="mono muted small">{{ number() }}</span>
          <span class="spacer"></span>
          <button matIconButton (click)="close.emit()" aria-label="Close quick view">
            <mat-icon>close</mat-icon>
          </button>
        </header>

        @if (loading()) {
          <app-loading />
        } @else if (task(); as d) {
          <div class="body">
            <h2 class="title">{{ d.title }}</h2>
            <div class="chips">
              <app-chip [value]="d.status" kind="status" />
              <app-chip [value]="d.priority" kind="priority" />
              @if (overdue()) { <span class="chip tone-danger">Overdue</span> }
            </div>

            <div class="fields">
              @if (d.clientName) { <app-field label="Client">{{ d.clientName }}</app-field> }
              <app-field label="Responsible person">
                {{ d.primaryAssigneeDisplayName ?? 'Nobody yet' }}
              </app-field>
              <app-field label="Type">{{ type(d.type) }}</app-field>
              <app-field label="Due">
                {{ d.dueDate ? (d.dueDate | date: 'mediumDate') : '—' }}
              </app-field>
              @if (d.totalWorkedTime !== '00:00:00') {
                <app-field label="Time logged">{{ d.totalWorkedTime | duration }}</app-field>
              }
              @if (d.blockedBy.length) {
                <app-field label="Waiting on">{{ d.blockedBy.join(', ') }}</app-field>
              }
            </div>

            <h3 class="sub">Description</h3>
            <p class="body-text">{{ d.description }}</p>
          </div>
        } @else if (request(); as d) {
          <div class="body">
            <h2 class="title">{{ d.title }}</h2>
            <div class="chips">
              <app-chip [value]="d.status" kind="requestStatus" />
              <app-chip [value]="d.requestedUrgency" kind="urgency" />
            </div>

            <div class="fields">
              @if (d.clientName) { <app-field label="Client">{{ d.clientName }}</app-field> }
              <app-field label="Raised by">{{ d.requestedByDisplayName }}</app-field>
              <app-field label="Raised">{{ since(d.requestedAt) }} ago</app-field>
              <app-field label="Type">{{ type(d.type) }}</app-field>
              @if (d.batchNumber) {
                <app-field label="Asked for with">
                  {{ d.batchNumber }} — item {{ d.ordinalInBatch }} of {{ d.batchItemCount }}
                </app-field>
              }
            </div>

            @if (d.progress; as p) {
              <h3 class="sub">Progress</h3>
              <p class="body-text">
                {{ p.statusLabel }}@if (p.responsibleDisplayName) { — {{ p.responsibleDisplayName }} }
              </p>
              @if (p.latestUpdate) {
                <p class="muted small">{{ p.latestUpdate }}</p>
              }
            }

            <h3 class="sub">Description</h3>
            <p class="body-text">{{ d.description }}</p>
          </div>
        } @else {
          <div class="body"><p class="muted">That is not available.</p></div>
        }

        <footer class="foot">
          <a matButton="filled" class="full" [routerLink]="fullPage()" (click)="close.emit()">
            Open the full page
          </a>
        </footer>
      </aside>
    }
  `,
  styles: `
    /* qv- prefixed: the shell already has a .scrim for its nav drawer, and two elements sharing
       one class name on the same page is a trap for whoever reads either of them next. */
    .qv-scrim {
      position: fixed; inset: 0; background: rgba(0, 0, 0, 0.28); z-index: 40;
    }
    .drawer {
      position: fixed; top: 0; right: 0; bottom: 0; width: min(430px, 92vw); z-index: 41;
      background: var(--surface-raised); border-left: 1px solid var(--border);
      box-shadow: -8px 0 28px rgba(0, 0, 0, 0.14);
      display: flex; flex-direction: column;
    }
    .head {
      display: flex; align-items: center; gap: 8px;
      padding: 10px 10px 10px 20px; border-bottom: 1px solid var(--border);
    }
    .body { flex: 1 1 auto; overflow-y: auto; padding: 18px 20px; }
    .title { font-size: 17px; font-weight: 600; margin: 0 0 10px; letter-spacing: -0.01em; }
    .chips { display: flex; gap: 7px; flex-wrap: wrap; margin-bottom: 14px; }
    .fields { display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px; }
    .sub {
      font-size: 11.5px; font-weight: 600; letter-spacing: 0.03em; text-transform: uppercase;
      color: var(--text-muted); margin: 16px 0 4px;
    }
    .foot { padding: 14px 20px; border-top: 1px solid var(--border); }
    .full { width: 100%; }
  `,
})
export class QuickViewComponent {
  private readonly api = inject(ApiService);

  readonly target = input<QuickViewTarget | null>(null);
  readonly close = output<void>();

  readonly task = signal<TaskDetailDto | null>(null);
  readonly request = signal<RequestDetailDto | null>(null);
  readonly loading = signal(false);

  type = (value: string) => requestTypeLabel(value as never);
  readonly since = sinceLabel;

  readonly number = computed(() =>
    this.task()?.taskNumber ?? this.request()?.requestNumber ?? '');

  readonly fullPage = computed(() => {
    const t = this.target();
    if (!t) return ['/'];
    return t.kind === 'task' ? ['/tasks', t.id] : ['/requests', t.id];
  });

  readonly overdue = computed(() => {
    const d = this.task();
    return !!d?.dueDate && new Date(d.dueDate) < new Date() && d.status !== 'Closed';
  });

  constructor() {
    effect(() => {
      const t = this.target();

      this.task.set(null);
      this.request.set(null);
      if (!t) return;

      this.loading.set(true);

      // The same call the full page makes. Not a lighter endpoint of its own: a second projection
      // of the same record is a second thing to keep true, and this is a panel that will be open
      // for four seconds.
      //
      // The two branches are kept apart rather than joined into one observable — the union of the
      // two response types has no callable `subscribe`, and casting it away would only hide that.
      if (t.kind === 'task') {
        this.api.task(t.id).subscribe({
          next: (d) => { this.task.set(d); this.loading.set(false); },
          error: () => this.loading.set(false),
        });
      } else {
        this.api.request(t.id).subscribe({
          next: (d) => { this.request.set(d); this.loading.set(false); },
          error: () => this.loading.set(false),
        });
      }
    });
  }
}
