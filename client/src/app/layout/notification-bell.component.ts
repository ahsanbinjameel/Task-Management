import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatBadgeModule } from '@angular/material/badge';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDividerModule } from '@angular/material/divider';
import { ApiService } from '../core/api.service';
import { RealtimeService } from '../core/realtime.service';
import { NotificationDto } from '../core/models';

@Component({
  selector: 'app-notification-bell',
  standalone: true,
  imports: [
    MatIconModule, MatButtonModule, MatMenuModule, MatBadgeModule, MatTooltipModule,
    MatDividerModule, DatePipe,
  ],
  template: `
    <button matIconButton [matMenuTriggerFor]="menu" (menuOpened)="load()"
            [matBadge]="unread() > 99 ? '99+' : unread()" [matBadgeHidden]="unread() === 0"
            matBadgeColor="warn" matBadgeSize="small"
            [matTooltip]="tooltip()" aria-label="Notifications">
      <mat-icon>{{ unread() > 0 ? 'notifications_active' : 'notifications' }}</mat-icon>
    </button>

    <mat-menu #menu="matMenu" class="bell-menu">
      <div class="head" (click)="$event.stopPropagation()">
        <strong>Notifications</strong>
        <span class="spacer"></span>
        @if (unread() > 0) {
          <button matButton (click)="markAllRead()">Mark all read</button>
        }
      </div>
      <mat-divider />

      @if (loading()) {
        <div class="state muted small">Loading…</div>
      } @else if (items().length === 0) {
        <div class="state muted small">Nothing here yet.</div>
      } @else {
        @for (n of items(); track n.id) {
          <button mat-menu-item class="item" [class.unread]="!n.isRead" (click)="open(n)">
            <div class="item-body">
              <span class="title">{{ n.title }}</span>
              @if (n.body) { <span class="muted small truncate">{{ n.body }}</span> }
              <span class="muted small">{{ n.createdAt | date: 'MMM d, HH:mm' }}</span>
            </div>
          </button>
        }
      }
    </mat-menu>
  `,
  styles: `
    .head { display: flex; align-items: center; gap: 8px; padding: 10px 8px 10px 16px; }
    .state { padding: 22px 16px; text-align: center; }
    .item { height: auto; padding-top: 9px; padding-bottom: 9px; }
    .item-body { display: flex; flex-direction: column; gap: 1px; max-width: 320px; }
    .title { font-size: 13.5px; line-height: 1.35; white-space: normal; }
    .item.unread { background: var(--tone-running-bg); }
    .item.unread .title { font-weight: 600; }
  `,
})
export class NotificationBellComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly realtime = inject(RealtimeService);
  private readonly router = inject(Router);

  readonly items = signal<NotificationDto[]>([]);
  readonly unread = signal(0);
  readonly loading = signal(false);

  tooltip = () => (this.unread() === 0 ? 'Notifications' : `${this.unread()} unread`);

  ngOnInit(): void {
    this.refreshCount();

    // Pushed, not polled. The badge is the one thing that has to be right without a refresh.
    this.realtime.notificationRaised.subscribe(() => this.refreshCount());
  }

  refreshCount(): void {
    this.api.unreadCount().subscribe({
      next: (r) => this.unread.set(r.count),
      error: () => undefined,
    });
  }

  load(): void {
    this.loading.set(true);
    this.api.notifications(false, 1, 15).subscribe({
      next: (page) => {
        this.items.set(page.items);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  markAllRead(): void {
    this.api.markAllRead().subscribe(() => {
      this.items.update((list) => list.map((n) => ({ ...n, isRead: true })));
      this.unread.set(0);
    });
  }

  /** A notification is a pointer: mark it read, then follow it to the thing it is about. */
  open(notification: NotificationDto): void {
    if (!notification.isRead) {
      this.api.markRead([notification.id]).subscribe(() => this.refreshCount());
    }

    if (notification.linkEntityType === 'Task' && notification.linkEntityId) {
      void this.router.navigate(['/tasks', notification.linkEntityId]);
    } else if (notification.linkEntityType === 'Request' && notification.linkEntityId) {
      void this.router.navigate(['/requests', notification.linkEntityId]);
    }
  }
}
