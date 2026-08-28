import { MatDialog } from '@angular/material/dialog';
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
import { ActAsDialog } from './act-as-dialog.component';
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
      <app-page-header title="Settings" />

      <!-- --- who you are ------------------------------------------------------------------ -->
      <section class="card card-pad">
        <h2 class="card-title">Your details</h2>

        <form [formGroup]="profile" (ngSubmit)="saveProfile()">
          <mat-form-field class="full">
            <mat-label>Full name</mat-label>
            <input matInput formControlName="displayName" maxlength="200" />
          </mat-form-field>

          <mat-form-field class="full">
            <mat-label>Email (optional)</mat-label>
            <input matInput type="email" formControlName="email" maxlength="256" />
          </mat-form-field>

          <div class="row">
            <span class="spacer"></span>
            <button matButton="filled" type="submit"
                    [disabled]="profile.invalid || profile.pristine || savingProfile()">
              {{ savingProfile() ? 'Saving...' : 'Save details' }}
            </button>
          </div>
        </form>

        <div class="facts locked">
          <div>
            <span class="muted small">Username</span>
            <strong>{{ auth.user()?.userName }}</strong>
          </div>
          <div>
            <span class="muted small">What you can do</span>
            <strong>{{ roles() }}</strong>
          </div>
        </div>

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
            <!--
              Acting as somebody else. A button rather than a link, because it changes the session
              you are in rather than taking you to a page — and it says plainly that the work is
              real, since the one dangerous misreading of this feature is "it is only a preview".
            -->
            @if (auth.has(Perm.adminImpersonate)) {
              <button type="button" class="link-row" (click)="actAs()">
                <mat-icon>visibility</mat-icon>
                <span>
                  <strong>Act as somebody else</strong><br />
                  <span class="muted small">
                    See the app as they see it. Real work, recorded as theirs with your name
                    alongside
                  </span>
                </span>
              </button>
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
    .facts.locked { margin-top: 18px; padding-top: 14px; border-top: 1px solid var(--border); }
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

    /* One of these rows is a button rather than a link — it changes the session you are in rather
       than taking you somewhere. The UA stylesheet gives a button its own font, centring and
       background, none of which it should keep here. */
    button.link-row {
      width: 100%; text-align: left; font: inherit;
      background: none; cursor: pointer;
    }
    button.link-row:hover { background: var(--surface-sunken); }
  `,
})
export class SettingsComponent {
  private readonly dialog = inject(MatDialog);
  readonly Perm = Perm;
  readonly auth = inject(AuthService);

  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly busy = signal(false);
  readonly savingProfile = signal(false);
  readonly rail = signal(readRailPreference());

  /**
   * Your own name and email.
   *
   * Username is not here on purpose: it is what you sign in with and what colleagues know you by,
   * and letting someone change it unannounced would let them quietly become a different person on
   * every screen. An administrator can, from the people list.
   */
  readonly profile = inject(FormBuilder).nonNullable.group({
    displayName: ['', [Validators.required, Validators.maxLength(200)]],
    email: ['', [Validators.email, Validators.maxLength(256)]],
  });

  saveProfile(): void {
    if (this.profile.invalid) return;
    this.savingProfile.set(true);

    const { displayName, email } = this.profile.getRawValue();

    this.api.updateMyProfile({ displayName: displayName.trim(), email: email.trim() || null })
      .subscribe({
        next: (user) => {
          this.savingProfile.set(false);
          // The shell shows the name in the corner; refresh the session copy so it updates now
          // rather than at the next sign-in.
          this.auth.applyProfile(user);
          this.profile.markAsPristine();
          this.toast.success('Your details have been saved.');
        },
        error: () => this.savingProfile.set(false),
      });
  }

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

  constructor() {
    const me = this.auth.user();
    this.profile.setValue({ displayName: me?.displayName ?? '', email: me?.email ?? '' });
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
  /** Opens the picker. The dialog performs the switch and navigates, so nothing is needed here. */
  actAs(): void {
    this.dialog.open(ActAsDialog, { width: 'min(520px, 92vw)', maxWidth: '92vw' });
  }

}
