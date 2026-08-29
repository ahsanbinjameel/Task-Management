import { Component, computed, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialog } from '@angular/material/dialog';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { ToastService } from '../core/toast.service';
import { DemoUserDto } from '../core/models';
import { ConfirmDialog, ConfirmData } from '../shared/dialogs';

/**
 * Change who is being shown, from the header, during a demonstration.
 *
 * Starting one is not here — that is an administrative act and lives in Settings. Switching is the
 * opposite: something done repeatedly while talking to somebody, and a control you have to navigate
 * to breaks that mid-sentence.
 *
 * Everything it offers runs against the demo database and nothing else. The switch is a new token,
 * not a sign-in, so it costs a click and no re-authentication.
 */
@Component({
  selector: 'app-demo-switcher',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatMenuModule],
  template: `
    <!--
      Only while a demonstration is running. Starting one is an administrative act and lives in
      Settings; switching between the cast is something you do repeatedly *during* a demonstration,
      mid-sentence — "here is the requester's view, and here is the same request as the reviewer
      sees it" — so that one belongs in the header where it costs no navigation.
    -->
    @if (auth.inDemoMode()) {
      <!--
        Labelled "Switch user" rather than with the current name. The banner already says who is
        being shown and the account menu says it again — a third copy would be noise, and it would
        also hide what the control is *for*, which is the one thing a person reaching for it wants
        to know.
      -->
      <button matButton class="demo-trigger active" [matMenuTriggerFor]="menu">
        <mat-icon>groups</mat-icon>
        <span class="who">Switch user</span>
        <mat-icon iconPositionEnd>expand_more</mat-icon>
      </button>

      <mat-menu #menu="matMenu" class="demo-menu">
        <div class="menu-head" (click)="$event.stopPropagation()">
          <strong>Demo mode</strong>
          <span class="muted small">Separate data. Nothing here touches live.</span>
        </div>

        @for (person of cast(); track person.id) {
          <button mat-menu-item (click)="switchTo(person)"
                  [disabled]="person.userName === currentUserName()">
            <mat-icon>{{ person.userName === currentUserName() ? 'check' : 'person' }}</mat-icon>
            <span class="entry">
              <span>{{ person.displayName }}</span>
              <span class="muted small">{{ person.purpose }}</span>
            </span>
          </button>
        }

        <div class="menu-sep"></div>

        <button mat-menu-item (click)="reset()">
          <mat-icon>restart_alt</mat-icon><span>Reset demo data</span>
        </button>
        <button mat-menu-item (click)="exit()">
          <mat-icon>logout</mat-icon><span>Leave demo mode</span>
        </button>
      </mat-menu>
    }
  `,
  styles: `
    .demo-trigger { --mdc-text-button-label-text-color: var(--text); }
    .demo-trigger.active {
      background: var(--tone-warn-bg);
      --mdc-text-button-label-text-color: var(--tone-warn-fg);
    }
    .who, .label { max-width: 160px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    @media (max-width: 700px) { .who, .label { display: none; } }

    .menu-head {
      display: flex; flex-direction: column; gap: 2px;
      padding: 10px 16px 8px; cursor: default;
    }
    .menu-sep { height: 1px; background: var(--border); margin: 4px 0; }
    .entry { display: flex; flex-direction: column; line-height: 1.3; }
  `,
})
export class DemoSwitcherComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly dialog = inject(MatDialog);
  readonly auth = inject(AuthService);

  readonly cast = signal<DemoUserDto[]>([]);
  readonly currentUserName = computed(() => this.auth.user()?.userName ?? '');

  constructor() {
    /*
     * Loaded when a demonstration *starts*, not when the shell is built.
     *
     * The shell — and therefore this component — is constructed once, at sign-in, when demo mode is
     * necessarily off. A one-time check in the constructor therefore never fired, and the menu
     * opened with the reset and exit items and no cast at all. An effect re-runs when the signal
     * changes, which is the moment there is actually something to fetch.
     */
    effect(() => {
      if (this.auth.inDemoMode()) this.refreshCast();
      else this.cast.set([]);
    });
  }

  switchTo(person: DemoUserDto): void {
    this.auth.switchDemoUser(person.id).subscribe(() => {
      this.toast.success(`Now showing ${person.displayName}.`);
      // Home, because half the screens the previous role could open this one cannot.
      void this.router.navigateByUrl('/');
    });
  }

  exit(): void {
    this.auth.exitDemo().subscribe(() => {
      this.toast.success('Back to your own account.');
      void this.router.navigateByUrl('/');
    });
  }

  reset(): void {
    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: {
          title: 'Reset the demo data?',
          message:
            'Everything created during demonstrations is deleted and the demo team starts fresh. '
            + 'Live data is in a different database and is not touched.',
          confirmText: 'Reset it',
          danger: true,
        },
      })
      .afterClosed()
      .subscribe((confirmed?: boolean) => {
        if (!confirmed) return;

        this.api.resetDemo().subscribe(() => {
          this.toast.success('Demo data reset.');
          // The demo user ids are new after a rebuild, so the session in hand is stale.
          this.auth.restoreLiveSession();
          void this.router.navigateByUrl('/');
        });
      });
  }

  private refreshCast(): void {
    this.api.demoStatus().subscribe({
      next: (status) => this.cast.set(status.cast),
      error: () => undefined,
    });
  }
}
