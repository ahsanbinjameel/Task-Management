import { Component, Input, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Priority, WorkTaskStatus, WorkforceState } from '../core/models';
import { humanizeEnum, priorityTone, statusTone, workforceTone } from '../core/format';

/** A status, priority or availability value rendered as a tone-coded chip. */
@Component({
  selector: 'app-chip',
  standalone: true,
  template: `<span class="chip" [class]="'tone-' + tone()" [class.dot]="dot()">{{ label() }}</span>`,
})
export class ChipComponent {
  readonly value = input.required<string>();
  readonly kind = input<'status' | 'priority' | 'workforce' | 'plain'>('plain');
  readonly dot = input(false);

  label = () => humanizeEnum(this.value());

  tone = () => {
    switch (this.kind()) {
      case 'status': return statusTone(this.value() as WorkTaskStatus);
      case 'priority': return priorityTone(this.value() as Priority);
      case 'workforce': return workforceTone(this.value() as WorkforceState);
      default: return 'neutral';
    }
  };
}

/** Page title, optional subtitle, and a slot for the actions that belong to the page. */
@Component({
  selector: 'app-page-header',
  standalone: true,
  template: `
    <header class="header">
      <div class="titles">
        <h1>{{ title() }}</h1>
        @if (subtitle()) { <p class="muted">{{ subtitle() }}</p> }
      </div>
      <div class="actions"><ng-content /></div>
    </header>
  `,
  styles: `
    .header {
      display: flex; align-items: flex-start; gap: 16px;
      flex-wrap: wrap; margin-bottom: 20px;
    }
    .titles { flex: 1 1 320px; min-width: 0; }
    h1 { font-size: 22px; font-weight: 600; margin: 0 0 2px; letter-spacing: -0.01em; }
    p { margin: 0; font-size: 13.5px; }
    .actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  `,
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly subtitle = input<string>();
}

/**
 * What a list shows when it has nothing. Worth a component of its own: "no results" and "nothing
 * assigned to you yet" mean very different things, and a blank table says neither.
 */
@Component({
  selector: 'app-empty',
  standalone: true,
  imports: [MatIconModule],
  template: `
    <div class="empty">
      <mat-icon>{{ icon() }}</mat-icon>
      <p class="title">{{ message() }}</p>
      @if (hint()) { <p class="muted small">{{ hint() }}</p> }
      <ng-content />
    </div>
  `,
  styles: `
    .empty {
      display: flex; flex-direction: column; align-items: center; gap: 6px;
      padding: 44px 20px; text-align: center; color: var(--text-muted);
    }
    mat-icon { width: 40px; height: 40px; font-size: 40px; opacity: 0.4; }
    .title { margin: 6px 0 0; font-size: 14.5px; font-weight: 500; color: var(--text); }
    p { margin: 0; }
  `,
})
export class EmptyComponent {
  readonly message = input.required<string>();
  readonly hint = input<string>();
  readonly icon = input('inbox');
}

@Component({
  selector: 'app-loading',
  standalone: true,
  imports: [MatProgressSpinnerModule],
  template: `
    <div class="loading">
      <mat-spinner diameter="34" />
      @if (message()) { <span class="muted small">{{ message() }}</span> }
    </div>
  `,
  styles: `
    .loading {
      display: flex; flex-direction: column; align-items: center;
      gap: 12px; padding: 48px 20px;
    }
  `,
})
export class LoadingComponent {
  readonly message = input<string>();
}

/** A labelled read-only value. Used all over the detail screens. */
@Component({
  selector: 'app-field',
  standalone: true,
  template: `
    <div class="field">
      <span class="label">{{ label() }}</span>
      <span class="value"><ng-content /></span>
    </div>
  `,
  styles: `
    .field { display: flex; flex-direction: column; gap: 2px; padding: 8px 0; min-width: 0; }
    .label {
      font-size: 11.5px; font-weight: 600; letter-spacing: 0.03em;
      text-transform: uppercase; color: var(--text-muted);
    }
    .value { font-size: 14px; min-width: 0; word-break: break-word; }
  `,
})
export class FieldComponent {
  readonly label = input.required<string>();
}

/** A headline number for the dashboards. */
@Component({
  selector: 'app-stat',
  standalone: true,
  imports: [MatIconModule],
  template: `
    <div class="stat" [class.accent]="accent()">
      <span class="label">{{ label() }}</span>
      <span class="value mono">{{ value() }}</span>
      @if (hint()) { <span class="hint muted small">{{ hint() }}</span> }
    </div>
  `,
  styles: `
    .stat {
      display: flex; flex-direction: column; gap: 2px;
      padding: 16px 18px; background: var(--surface-raised);
      border: 1px solid var(--border); border-radius: var(--radius);
      box-shadow: var(--shadow-card);
    }
    .stat.accent { border-color: var(--tone-danger-fg); }
    .label {
      font-size: 11.5px; font-weight: 600; letter-spacing: 0.03em;
      text-transform: uppercase; color: var(--text-muted);
    }
    .value { font-size: 26px; font-weight: 600; line-height: 1.2; letter-spacing: -0.02em; }
    .stat.accent .value { color: var(--tone-danger-fg); }
  `,
})
export class StatComponent {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();
  readonly hint = input<string>();
  /** Draws attention when the number is a problem, e.g. overdue work. */
  readonly accent = input(false);
}
