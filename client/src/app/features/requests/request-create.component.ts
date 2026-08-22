import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { RequestType, RequestedUrgency } from '../../core/models';
import { humanizeEnum } from '../../core/format';
import { PageHeaderComponent } from '../../shared/ui';

@Component({
  selector: 'app-request-create',
  standalone: true,
  providers: [provideNativeDateAdapter()],
  imports: [
    ReactiveFormsModule, MatButtonModule, MatDatepickerModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatSelectModule, PageHeaderComponent,
  ],
  template: `
    <div class="page narrow">
      <app-page-header title="New request"
                       subtitle="Describe what you need. A reviewer decides what happens next." />

      <form class="card card-pad stack" [formGroup]="form" (ngSubmit)="submit()">
        <mat-form-field class="full">
          <mat-label>Title</mat-label>
          <input matInput formControlName="title" placeholder="Short summary of what you need" />
        </mat-form-field>

        <mat-form-field class="full">
          <mat-label>Description</mat-label>
          <textarea matInput rows="5" formControlName="description"></textarea>
          <mat-hint>The more precise this is, the less back-and-forth at review.</mat-hint>
        </mat-form-field>

        <div class="form-grid">
          <mat-form-field>
            <mat-label>Type</mat-label>
            <mat-select formControlName="type">
              @for (t of types; track t) { <mat-option [value]="t">{{ label(t) }}</mat-option> }
            </mat-select>
          </mat-form-field>

          <mat-form-field>
            <mat-label>Urgency</mat-label>
            <mat-select formControlName="requestedUrgency">
              @for (u of urgencies; track u) { <mat-option [value]="u">{{ u }}</mat-option> }
            </mat-select>
            <mat-hint>Advisory — triage sets the real priority.</mat-hint>
          </mat-form-field>

          <mat-form-field>
            <mat-label>Needed by (optional)</mat-label>
            <input matInput [matDatepicker]="picker" formControlName="targetDate" />
            <mat-datepicker-toggle matIconSuffix [for]="picker" />
            <mat-datepicker #picker />
          </mat-form-field>
        </div>

        <mat-form-field class="full">
          <mat-label>Business impact (optional)</mat-label>
          <textarea matInput rows="2" formControlName="businessImpact"
                    placeholder="What does it cost while this is not done?"></textarea>
        </mat-form-field>

        <div class="form-grid">
          <mat-form-field>
            <mat-label>Expected result (optional)</mat-label>
            <textarea matInput rows="2" formControlName="expectedResult"></textarea>
          </mat-form-field>
          <mat-form-field>
            <mat-label>What happens instead (optional)</mat-label>
            <textarea matInput rows="2" formControlName="currentResult"></textarea>
          </mat-form-field>
        </div>

        <mat-form-field class="full">
          <mat-label>Steps to reproduce (optional)</mat-label>
          <textarea matInput rows="3" formControlName="reproductionSteps"></textarea>
        </mat-form-field>

        <div class="row">
          <span class="spacer"></span>
          <button matButton type="button" (click)="cancel()">Cancel</button>
          <button matButton="filled" type="submit" [disabled]="form.invalid || busy()">
            Submit request
          </button>
        </div>
      </form>
    </div>
  `,
  styles: `
    .narrow { max-width: 880px; }
    .full { width: 100%; }
  `,
})
export class RequestCreateComponent {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  readonly busy = signal(false);

  readonly types: RequestType[] = [
    'Bug', 'ChangeRequest', 'NewFeature', 'Support', 'Configuration', 'Database', 'Report',
    'Investigation', 'DataCorrection', 'Infrastructure', 'Other',
  ];
  readonly urgencies: RequestedUrgency[] = ['Critical', 'High', 'Normal', 'Low'];

  readonly form = inject(FormBuilder).nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(300)]],
    description: ['', [Validators.required, Validators.maxLength(8000)]],
    type: ['Support' as RequestType, Validators.required],
    requestedUrgency: ['Normal' as RequestedUrgency, Validators.required],
    targetDate: [null as Date | null],
    businessImpact: [''],
    expectedResult: [''],
    currentResult: [''],
    reproductionSteps: [''],
  });

  label = (value: string) => humanizeEnum(value);

  submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);

    const v = this.form.getRawValue();

    this.api.createRequest({
      title: v.title.trim(),
      description: v.description.trim(),
      type: v.type,
      requestedUrgency: v.requestedUrgency,
      targetDate: v.targetDate ? v.targetDate.toISOString() : null,
      businessImpact: v.businessImpact.trim() || undefined,
      expectedResult: v.expectedResult.trim() || undefined,
      currentResult: v.currentResult.trim() || undefined,
      reproductionSteps: v.reproductionSteps.trim() || undefined,
    }).subscribe({
      next: (created) => {
        this.busy.set(false);
        this.toast.success(`${created.requestNumber} submitted.`);
        void this.router.navigate(['/requests', created.id]);
      },
      error: () => this.busy.set(false),
    });
  }

  cancel(): void {
    void this.router.navigate(['/requests']);
  }
}
