import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { AuthService } from '../../core/auth.service';
import { describe } from '../../core/http.interceptors';
import { HttpErrorResponse } from '@angular/common/http';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [
    ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatProgressBarModule,
  ],
  template: `
    <div class="wrap">
      <div class="panel card">
        @if (busy()) { <mat-progress-bar mode="indeterminate" /> }

        <div class="pad">
          <div class="brand">
            <mat-icon>account_tree</mat-icon>
            <span>WorkflowApp</span>
          </div>
          <p class="muted">Sign in to continue.</p>

          <form [formGroup]="form" (ngSubmit)="submit()">
            <mat-form-field class="full">
              <mat-label>Username</mat-label>
              <input matInput formControlName="userName" autocomplete="username" autofocus />
            </mat-form-field>

            <mat-form-field class="full">
              <mat-label>Password</mat-label>
              <input matInput type="password" formControlName="password"
                     autocomplete="current-password" />
            </mat-form-field>

            @if (error()) {
              <div class="error small">
                <mat-icon>error_outline</mat-icon>
                <span>{{ error() }}</span>
              </div>
            }

            <button matButton="filled" class="full submit" type="submit"
                    [disabled]="form.invalid || busy()">
              Sign in
            </button>
          </form>
        </div>
      </div>
    </div>
  `,
  styles: `
    .wrap {
      min-height: 100vh; display: grid; place-items: center; padding: 24px;
      background: radial-gradient(1200px 600px at 50% -10%, #1d3b5e 0%, #0d1b2b 60%);
    }
    .panel { width: min(400px, 100%); overflow: hidden; }
    .pad { padding: 30px 28px 28px; }
    .brand {
      display: flex; align-items: center; gap: 9px;
      font-size: 19px; font-weight: 600; letter-spacing: -0.01em;
    }
    .brand mat-icon { color: #1d69d4; }
    .muted { margin: 4px 0 22px; font-size: 13.5px; }
    .full { width: 100%; }
    .submit { height: 44px; margin-top: 6px; }
    .error {
      display: flex; align-items: center; gap: 7px; margin-bottom: 12px;
      padding: 9px 11px; border-radius: 8px;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .error mat-icon { font-size: 18px; width: 18px; height: 18px; }
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly busy = signal(false);
  readonly error = signal('');

  readonly form = inject(FormBuilder).nonNullable.group({
    userName: ['', Validators.required],
    password: ['', Validators.required],
  });

  submit(): void {
    if (this.form.invalid) return;

    this.busy.set(true);
    this.error.set('');

    const { userName, password } = this.form.getRawValue();

    this.auth.login(userName, password).subscribe({
      next: () => {
        this.busy.set(false);
        // Resume wherever the guard interrupted, so an expired session is barely a detour.
        const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/';
        void this.router.navigateByUrl(returnUrl);
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.error.set(
          err instanceof HttpErrorResponse ? describe(err) : 'Could not sign in.',
        );
      },
    });
  }
}
