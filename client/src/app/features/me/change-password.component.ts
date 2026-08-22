import { Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { PageHeaderComponent } from '../../shared/ui';

const matching = (group: AbstractControl): ValidationErrors | null =>
  group.get('newPassword')?.value === group.get('confirm')?.value ? null : { mismatch: true };

@Component({
  selector: 'app-change-password',
  standalone: true,
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, PageHeaderComponent],
  template: `
    <div class="page narrow">
      <app-page-header title="Change password" />

      <form class="card card-pad" [formGroup]="form" (ngSubmit)="submit()">
        <mat-form-field class="full">
          <mat-label>Current password</mat-label>
          <input matInput type="password" formControlName="currentPassword"
                 autocomplete="current-password" />
        </mat-form-field>

        <mat-form-field class="full">
          <mat-label>New password</mat-label>
          <input matInput type="password" formControlName="newPassword" autocomplete="new-password" />
          <mat-hint>At least 10 characters.</mat-hint>
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
            Change password
          </button>
        </div>
      </form>
    </div>
  `,
  styles: `
    .narrow { max-width: 520px; }
    .full { width: 100%; }
  `,
})
export class ChangePasswordComponent {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly busy = signal(false);

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
        this.toast.success('Password changed.');
        void this.router.navigate(['/']);
      },
      error: () => this.busy.set(false),
    });
  }
}
