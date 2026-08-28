import { Component, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map, startWith } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatBadgeModule } from '@angular/material/badge';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatDividerModule } from '@angular/material/divider';
import { MatDialog } from '@angular/material/dialog';
import { AuthService } from '../core/auth.service';
import { NAV_GROUPS, NavGroup } from '../core/navigation';
import { NotificationBellComponent } from './notification-bell.component';
import { ShiftWidgetComponent } from './shift-widget.component';
import { ConfirmDialog, ConfirmData } from '../shared/dialogs';
import { readRailPreference, writeRailPreference } from './nav-preference';

/** A group the reader can actually reach, resolved to the one link that opens it. */
interface VisibleNavItem {
  label: string;
  icon: string;
  /** The first view in the group this reader may open. */
  route: string;
  /** Every view in the group, so the item stays lit while you are on any of them. */
  routes: string[];
}

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    RouterOutlet, RouterLink, MatIconModule, MatButtonModule, MatMenuModule,
    MatBadgeModule, MatTooltipModule, MatSidenavModule, MatDividerModule,
    NotificationBellComponent, ShiftWidgetComponent,
  ],
  template: `
    <div class="shell" [class.nav-open]="navOpen()" [class.rail]="collapsed()">
      <aside class="nav">
        <div class="brand">
          <mat-icon>account_tree</mat-icon>
          <span>WorkflowApp</span>
        </div>

        <nav>
          @for (item of items(); track item.label) {
            <a [routerLink]="item.route" [class.active]="isActive(item)"
               [matTooltip]="collapsed() ? item.label : ''" matTooltipPosition="right"
               (click)="navOpen.set(false)">
              <mat-icon>{{ item.icon }}</mat-icon>
              <span>{{ item.label }}</span>
            </a>
          }
        </nav>
      </aside>

      <div class="main">
        <header class="topbar">
          <button matIconButton class="burger" (click)="navOpen.set(!navOpen())"
                  aria-label="Toggle navigation">
            <mat-icon>menu</mat-icon>
          </button>

          <button matIconButton class="collapse" (click)="toggleRail()"
                  [matTooltip]="collapsed() ? 'Expand menu' : 'Collapse menu'">
            <mat-icon>{{ collapsed() ? 'chevron_right' : 'chevron_left' }}</mat-icon>
          </button>

          <div class="spacer"></div>

          <app-shift-widget />
          <app-notification-bell />

          <button matButton [matMenuTriggerFor]="userMenu" class="user-button">
            <span class="avatar">{{ initials() }}</span>
            <span class="user-name">{{ auth.displayName() }}</span>
            <mat-icon iconPositionEnd>expand_more</mat-icon>
          </button>

          <mat-menu #userMenu="matMenu">
            <div class="menu-head">
              <strong>{{ auth.displayName() }}</strong>
              <span class="muted small">{{ roles() }}</span>
            </div>
            <mat-divider />
            <!--
              "My day" is not here: it is a work screen, and it sits in the nav as a view inside
              My tasks. An item in two places is one the reader has to think about twice.
              Everything about the account or this browser lives behind Settings — including the
              administration screens, which is why the rail no longer carries an Admin section.
            -->
            <a mat-menu-item routerLink="/me/settings">
              <mat-icon>settings</mat-icon><span>Settings</span>
            </a>
            <mat-divider />
            <button mat-menu-item (click)="signOut()">
              <mat-icon>logout</mat-icon><span>Sign out</span>
            </button>
          </mat-menu>
        </header>

        <main><router-outlet /></main>
      </div>

      <!-- Closes the drawer when it is overlaying the page on a narrow screen. -->
      <div class="scrim" (click)="navOpen.set(false)"></div>
    </div>
  `,
  styleUrl: './shell.component.scss',
})
export class ShellComponent {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  readonly navOpen = signal(false);

  /**
   * Sign out sits one item below "Change password" in a small menu, and hitting it by accident
   * costs whatever was half-typed on the screen behind it. The shift is the part worth spelling
   * out: signing out does **not** end it, so someone who signs out thinking they have clocked off
   * stays on the clock until the stale-shift sweep closes it at their last sign of life.
   */
  signOut(): void {
    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: {
          title: 'Sign out?',
          message:
            'Anything you have typed and not saved is lost. Signing out does not end your shift — '
            + 'use "End my shift" first if you have finished for the day.',
          confirmText: 'Sign out',
        },
      })
      .afterClosed()
      .subscribe((confirmed?: boolean) => {
        if (confirmed) this.auth.logout();
      });
  }

  /** Collapsed to an icon rail, remembered across visits. Shared with the Settings page. */
  readonly collapsed = signal(readRailPreference());

  toggleRail(): void {
    const next = !this.collapsed();
    this.collapsed.set(next);
    writeRailPreference(next);
  }

  readonly initials = computed(() => {
    const name = this.auth.displayName().trim();
    if (!name) return '?';
    const parts = name.split(/\s+/).filter(Boolean);
    return (parts.length === 1 ? parts[0].slice(0, 2) : parts[0][0] + parts[parts.length - 1][0])
      .toUpperCase();
  });

  readonly roles = computed(() => this.auth.user()?.roles.join(', ') ?? '');

  /**
   * The sidebar the reader actually gets.
   *
   * A group appears when they can reach at least one view inside it, and the link goes to the
   * first such view — so "Team" opens Workload for a coordinator and Reports for someone who only
   * holds `Reports.View`, without either being offered a link that would bounce.
   *
   * There are no section headings any more. Six job-shaped items do not need to be filed under
   * "Work", "Intake", "Coordinate" and "Insight"; those headings described the schema, and they
   * were most of what made the rail feel like an index.
   */
  readonly items = computed<VisibleNavItem[]>(() =>
    NAV_GROUPS
      .map((group) => this.resolve(group))
      .filter((item): item is VisibleNavItem => item !== null));

  private resolve(group: NavGroup): VisibleNavItem | null {
    const reachable = group.views.filter(
      (view) => !view.permissions || this.auth.hasAny(...view.permissions));
    if (reachable.length === 0) return null;

    return {
      label: group.label,
      icon: group.icon,
      route: reachable[0].route,
      routes: reachable.map((view) => view.route),
    };
  }

  /** The URL, as a signal, so the highlight follows navigation without a subscription per link. */
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
      startWith(this.router.url)),
    { initialValue: this.router.url });

  /**
   * Lit while the reader is on *any* view in the group — `routerLinkActive` could not do this,
   * because it only knows the one route it was given, and the whole point of §11 is that the
   * assignment queue is a view of Tasks rather than a destination beside it.
   *
   * Home is matched exactly. Everything else matches the route or a path beneath it, so a task
   * detail keeps Tasks lit; the trailing-boundary test is what stops `/task` lighting `/tasks`.
   */
  isActive(item: VisibleNavItem): boolean {
    const url = this.url().split('?')[0].split('#')[0];

    return item.routes.some((route) => {
      if (route === '/') return url === '/';
      return url === route || url.startsWith(route + '/');
    });
  }
}
