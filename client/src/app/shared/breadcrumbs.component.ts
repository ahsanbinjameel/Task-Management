import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';

export interface Crumb {
  label: string;
  /** Omitted on the last crumb — you are already there. */
  route?: string | unknown[];
}

/**
 * Where this page sits, and the way back.
 *
 * Shows the real chain a piece of work travelled — the request it came from, the task it became —
 * rather than the URL segments, which describe the routing table and not the work. The browser
 * back button only retraces where *you* have been; this describes where the *thing* has been, so
 * someone who arrived from a notification can still walk up to its request.
 */
@Component({
  selector: 'app-breadcrumbs',
  standalone: true,
  imports: [RouterLink, MatIconModule],
  template: `
    <nav class="crumbs" aria-label="Breadcrumb">
      @for (c of crumbs(); track c.label; let last = $last) {
        @if (!last && c.route) {
          <a [routerLink]="c.route">{{ c.label }}</a>
          <mat-icon>chevron_right</mat-icon>
        } @else {
          <span class="here">{{ c.label }}</span>
        }
      }
    </nav>
  `,
  styles: `
    .crumbs {
      display: flex; align-items: center; flex-wrap: wrap; gap: 2px;
      font-size: 12.5px; color: var(--text-muted); margin-bottom: 8px;
    }
    .crumbs a { color: inherit; text-decoration: none; }
    .crumbs a:hover { color: var(--text); text-decoration: underline; }
    .crumbs mat-icon { font-size: 15px; width: 15px; height: 15px; opacity: .6; }
    .here { color: var(--text); font-weight: 500; }
  `,
})
export class BreadcrumbsComponent {
  readonly crumbs = input.required<Crumb[]>();
}
