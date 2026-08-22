import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatBadgeModule } from '@angular/material/badge';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatDividerModule } from '@angular/material/divider';
import { AuthService } from '../core/auth.service';
import { Perm } from '../core/permissions';
import { NotificationBellComponent } from './notification-bell.component';
import { ShiftWidgetComponent } from './shift-widget.component';

interface NavItem {
  label: string;
  icon: string;
  route: string;
  /** Shown when the user holds any of these. Empty means everyone. */
  permissions?: string[];
  section: string;
}

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, MatIconModule, MatButtonModule, MatMenuModule,
    MatBadgeModule, MatTooltipModule, MatSidenavModule, MatDividerModule,
    NotificationBellComponent, ShiftWidgetComponent,
  ],
  template: `
    <div class="shell" [class.nav-open]="navOpen()">
      <aside class="nav">
        <div class="brand">
          <mat-icon>account_tree</mat-icon>
          <span>WorkflowApp</span>
        </div>

        <nav>
          @for (section of sections(); track section.name) {
            <div class="section-label">{{ section.name }}</div>
            @for (item of section.items; track item.route) {
              <a [routerLink]="item.route" routerLinkActive="active"
                 [routerLinkActiveOptions]="{ exact: item.route === '/' }"
                 (click)="navOpen.set(false)">
                <mat-icon>{{ item.icon }}</mat-icon>
                <span>{{ item.label }}</span>
              </a>
            }
          }
        </nav>
      </aside>

      <div class="main">
        <header class="topbar">
          <button matIconButton class="burger" (click)="navOpen.set(!navOpen())"
                  aria-label="Toggle navigation">
            <mat-icon>menu</mat-icon>
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
            <a mat-menu-item routerLink="/me/day">
              <mat-icon>schedule</mat-icon><span>My day</span>
            </a>
            <a mat-menu-item routerLink="/me/password">
              <mat-icon>lock_reset</mat-icon><span>Change password</span>
            </a>
            <mat-divider />
            <button mat-menu-item (click)="auth.logout()">
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
  readonly navOpen = signal(false);

  readonly initials = computed(() => {
    const name = this.auth.displayName().trim();
    if (!name) return '?';
    const parts = name.split(/\s+/).filter(Boolean);
    return (parts.length === 1 ? parts[0].slice(0, 2) : parts[0][0] + parts[parts.length - 1][0])
      .toUpperCase();
  });

  readonly roles = computed(() => this.auth.user()?.roles.join(', ') ?? '');

  private readonly items: NavItem[] = [
    { section: 'Work', label: 'Dashboard', icon: 'dashboard', route: '/' },
    { section: 'Work', label: 'My queue', icon: 'checklist', route: '/my-queue',
      permissions: [Perm.taskWork] },
    { section: 'Work', label: 'Tasks', icon: 'task_alt', route: '/tasks' },
    { section: 'Work', label: 'My day', icon: 'schedule', route: '/me/day' },

    { section: 'Intake', label: 'Requests', icon: 'inbox', route: '/requests' },
    { section: 'Intake', label: 'Review queue', icon: 'rate_review', route: '/review-queue',
      permissions: [Perm.taskReview] },

    { section: 'Coordinate', label: 'Assignment queue', icon: 'assignment_ind', route: '/assignment',
      permissions: [Perm.taskAssign] },
    { section: 'Coordinate', label: 'Workload', icon: 'groups', route: '/workload',
      permissions: [Perm.workforceViewAll] },
    { section: 'Coordinate', label: "Who's working", icon: 'sensors', route: '/workforce',
      permissions: [Perm.workforceViewAll] },

    { section: 'Quality', label: 'QC queue', icon: 'verified', route: '/qc',
      permissions: [Perm.taskQCReview] },

    { section: 'Insight', label: 'Reports', icon: 'summarize', route: '/reports',
      permissions: [Perm.reportsView] },
    { section: 'Insight', label: 'Audit log', icon: 'policy', route: '/audit',
      permissions: [Perm.adminViewAudit] },

    { section: 'Admin', label: 'Users', icon: 'manage_accounts', route: '/admin/users',
      permissions: [Perm.adminManageUsers] },
    { section: 'Admin', label: 'Roles', icon: 'admin_panel_settings', route: '/admin/roles',
      permissions: [Perm.adminManageRoles] },
  ];

  /**
   * The menu is filtered by permission, so people see the tool they actually have rather than a
   * wall of links that 403. Sections with nothing in them disappear entirely.
   */
  readonly sections = computed(() => {
    const visible = this.items.filter(
      (item) => !item.permissions || this.auth.hasAny(...item.permissions),
    );

    const order = ['Work', 'Intake', 'Coordinate', 'Quality', 'Insight', 'Admin'];

    return order
      .map((name) => ({ name, items: visible.filter((i) => i.section === name) }))
      .filter((section) => section.items.length > 0);
  });
}
