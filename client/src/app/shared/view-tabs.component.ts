import { Component, computed, inject, input } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { navGroup } from '../core/navigation';

/**
 * The views inside one sidebar group, as a strip under the page title.
 *
 * This is the other half of the shrunk rail (PRODUCT-CORE §11). Once "Review queue" and
 * "Assignment queue" stop being their own sidebar entries they still have to be reachable, and the
 * honest place for them is beside the thing they are a view of: the review queue is a way of
 * looking at requests, so it sits on Requests.
 *
 * It reads the same `NAV_GROUPS` the sidebar reads, so a view cannot appear in one and not the
 * other, and it filters by the same permissions — a requester on the Requests page is not shown a
 * "To review" tab that would only bounce off the guard.
 *
 * Nothing renders when the reader can reach fewer than two of the views: a single tab is not a
 * choice, it is a label repeating the page title back at them.
 */
@Component({
  selector: 'app-view-tabs',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  template: `
    @if (views().length > 1) {
      <nav class="view-tabs">
        @for (view of views(); track view.route) {
          <a [routerLink]="view.route" routerLinkActive="active"
             [routerLinkActiveOptions]="{ exact: true }">{{ view.label }}</a>
        }
      </nav>
    }
  `,
  styles: `
    .view-tabs {
      display: flex; gap: 4px; flex-wrap: wrap;
      margin: -8px 0 18px; padding-bottom: 2px;
    }
    a {
      padding: 7px 13px; border-radius: 999px;
      font-size: 13.5px; font-weight: 500; text-decoration: none;
      color: var(--text-muted); white-space: nowrap;
    }
    a:hover { background: var(--surface-sunken); color: var(--text); }
    /* The current view reads as selected, not merely hovered — these sit next to each other and a
       tint alone is too easy to lose against the page. */
    a.active { background: #1d69d4; color: #fff; }
    a.active:hover { background: #1d69d4; color: #fff; }
  `,
})
export class ViewTabsComponent {
  private readonly auth = inject(AuthService);

  /** The `NAV_GROUPS` key this page belongs to. */
  readonly group = input.required<string>();

  readonly views = computed(() =>
    (navGroup(this.group())?.views ?? []).filter(
      (view) => !view.permissions || this.auth.hasAny(...view.permissions)));
}
