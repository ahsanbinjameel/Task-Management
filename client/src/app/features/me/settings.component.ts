import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { ToastService } from '../../core/toast.service';
import { Perm } from '../../core/permissions';
import { roleLabel } from '../../core/labels';
import { PageHeaderComponent } from '../../shared/ui';
import { readRailPreference, writeRailPreference } from '../../layout/nav-preference';

const matching = (group: AbstractControl): ValidationErrors | null =>
  group.get('newPassword')?.value === group.get('confirm')?.value ? null : { mismatch: true };

/**
 * One place for the things that are about *you and this machine* rather than about the work.
 *
 * These used to be loose items in the profile menu, which meant the menu grew every time a
 * preference was added and "where do I change X?" had no general answer. The menu now offers one
 * door; what is behind it is grouped by who it affects — your account, this browser, and (for the
 * people who run the system) the configuration screens that live elsewhere in the nav.
 *
 * Preferences that are genuinely per-browser stay in localStorage and say so on the page. Anything
 * belonging to the account would go through the API instead: a setting that quietly vanishes when
 * someone signs in on a different machine is a bug report waiting to happen.
 */
@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [
    RouterLink, ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatSlideToggleModule, PageHeaderComponent,
  ],
  template: `
    <div class="page narrow">
      <app-page-header title="Settings" subtitle="Your account and how this app behaves for you." />

      <!-- --- who you are ------------------------------------------------------------------ -->
      <section class="card card-pad">
        <h2 class="card-title">Account</h2>

        <div class="facts">
          <div>
            <span class="muted small">Name</span>
            <strong>{{ auth.displayName() }}</strong>
          </div>
          <div>
            <span class="muted small">Username</span>
            <strong>{{ auth.user()?.userName }}</strong>
          </div>
          <div>
            <span class="muted small">Email</span>
            <strong>{{ auth.user()?.email || '—' }}</strong>
          </div>
          <div>
            <span class="muted small">What you can do</span>
            <strong>{{ roles() }}</strong>
          </div>
        </div>

        <p class="muted small note">
          Your name, email and roles are set by an administrator — ask one of them if something
          here is wrong.
        </p>
      </section>

      <!-- --- password --------------------------------------------------------------------- -->
      <section class="card card-pad">
        <h2 class="card-title">Change password</h2>

        <form [formGroup]="form" (ngSubmit)="submit()">
          <mat-form-field class="full">
            <mat-label>Current password</mat-label>
            <input matInput type="password" formControlName="currentPassword"
                   autocomplete="current-password" />
          </mat-form-field>

          <mat-form-field class="full">
            <mat-label>New password (at least 10 characters)</mat-label>
            <input matInput type="password" formControlName="newPassword"
                   autocomplete="new-password" />
          </mat-form-field>

          <mat-form-field class="full">
            <mat-label>Confirm new password</mat-label>
            <input matInput type="password" formControlName="confirm" autocomplete="new-password" />
            @if (form.hasError('mismatch') && form.get('confirm')?.touched) {
              <mat-error>The two passwords do not match.</mat-error>
            }
          </mat-form-field>

          <div class="row">
            <span class="spacer"></span>
            <button matButton="filled" type="submit" [disabled]="form.invalid || busy()">
              {{ busy() ? 'Saving…' : 'Change password' }}
            </button>
          </div>
        </form>
      </section>

      <!-- --- this browser ----------------------------------------------------------------- -->
      <section class="card card-pad">
        <h2 class="card-title">Preferences</h2>

        <mat-slide-toggle [checked]="rail()" (change)="setRail($event.checked)">
          Keep the sidebar collapsed
        </mat-slide-toggle>
        <p class="muted small note">
          Shows the menu as icons only, for more room on the page.
        </p>

        <p class="muted small browser">
          <mat-icon>info_outline</mat-icon>
          Preferences are remembered in this browser only — signing in somewhere else starts from
          the defaults.
        </p>
      </section>

      <!-- --- running the system ------------------------------------------------------------ -->
      @if (canAdminister()) {
        <section class="card card-pad">
          <h2 class="card-title">System configuration</h2>
          <p class="muted small note">
            These change the system for everyone, not just for you.
          </p>

          <div class="links">
            @if (auth.has(Perm.adminManageUsers)) {
              <a class="link-row" routerLink="/admin/users">
                <mat-icon>group</mat-icon>
                <span>
                  <strong>People</strong><br />
                  <span class="muted small">
                    Add accounts, reset passwords, turn access on and off
                  </span>
                </span>
              </a>
            }
            @if (auth.has(Perm.adminManageRoles)) {
              <a class="link-row" routerLink="/admin/roles">
                <mat-icon>admin_panel_settings</mat-icon>
                <span>
                  <strong>Roles and permissions</strong><br />
                  <span class="muted small">What each role is allowed to do</span>
                </span>
              </a>
            }
            @if (auth.has(Perm.adminManageConfig)) {
              <a class="link-row" routerLink="/admin/setup">
                <mat-icon>tune</mat-icon>
                <span>
                  <strong>Setup data</strong><br />
                  <span class="muted small">
                    Clients, pause reasons, departments and teams
                  </span>
                </span>
              </a>
            }
            @if (auth.has(Perm.adminViewAudit)) {
              <a class="link-row" routerLink="/audit">
                <mat-icon>policy</mat-icon>
                <span>
                  <strong>Audit log</strong><br />
                  <span class="muted small">Every change, who made it and when</span>
                </span>
              </a>
            }
          </div>
        </section>
      }
    </div>
  `,
  styles: `
    .narrow { max-width: 620px; }
    .full { width: 100%; }
    section { margin-bottom: 16px; }
    .note { margin: 4px 0 0; }
    .facts { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); }
    .facts > div { display: flex; flex-direction: column; gap: 1px; }
    .browser { display: flex; align-items: flex-start; gap: 6px; margin: 12px 0 0; }
    .browser mat-icon { font-size: 16px; width: 16px; height: 16px; flex: none; margin-top: 2px; }
    .links { display: grid; gap: 8px; margin-top: 12px; }
    .link-row {
      display: flex; align-items: flex-start; gap: 10px; padding: 10px 12px;
      border: 1px solid var(--border); border-radius: 10px;
      color: inherit; text-decoration: none;
    }
    .link-row:hover { border-color: var(--border-strong); }
    .link-row mat-icon { flex: none; margin-top: 1px; color: var(--text-muted); }
  `,
})
export class SettingsComponent {
  readonly Perm = Perm;
  readonly auth = inject(AuthService);

  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly busy = signal(false);
  readonly rail = signal(readRailPreference());

  readonly roles = () =>
    (this.auth.user()?.roles ?? []).map(roleLabel).join(', ') || 'No roles yet';

  /** Only worth a heading if there is something under it. */
  readonly canAdminister = () =>
    this.auth.has(Perm.adminManageUsers)
    || this.auth.has(Perm.adminManageRoles)
    || this.auth.has(Perm.adminManageConfig)
    || this.auth.has(Perm.adminViewAudit);

  setRail(collapsed: boolean): void {
    this.rail.set(collapsed);
    writeRailPreference(collapsed);
  }

  readonly form = inject(FormBuilder).nonNullable.group({
    currentPassword: ['', Validators.required],
    newPassword: ['', [Validators.required, Validators.minLength(10)]],
    confirm: ['', Validators.required],
  }, { validators: matching });

  submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);

    const { currentPassword, newPassword } = this.form.getRawValue();

    this.api.changePassword(currentPassword, newPassword).subscribe({
      next: () => {
        this.busy.set(false);
        // Deliberately stays on the page rather than bouncing to the dashboard, which is what the
        // old standalone screen did: navigating away reads as "something happened" without saying
        // what, and there is more on this page the user may still want.
        this.form.reset();
        this.toast.success('Password changed.');
      },
      error: () => this.busy.set(false),
    });
  }
}
