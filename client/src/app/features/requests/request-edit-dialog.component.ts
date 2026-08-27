import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ApiService } from '../../core/api.service';
import { FormSubmit } from '../../core/form-submit';
import { RequestDetailDto, RequestType, RequestedUrgency } from '../../core/models';
import { enumOptions, SearchSelectComponent } from '../../shared/search-select.component';

/**
 * Lets the requester correct their own request while it is still waiting to be looked at.
 *
 * The server decides whether that is allowed — editing stops once triage has acted, because a
 * decision made against text that then changed is worse than no decision. It also records what
 * changed and tells the reviewers, so an edit cannot slide past someone who already read it.
 */
@Component({
  selector: 'app-request-edit-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule,
    MatIconModule, MatAutocompleteModule, SearchSelectComponent,
  ],
  template: `
    <h2 mat-dialog-title>Change this request</h2>
    <mat-dialog-content>
      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <mat-form-field class="full">
        <mat-label>Title</mat-label>
        <input matInput name="title" [(ngModel)]="title" />
        @if (form.fieldError('title'); as e) { <mat-error>{{ e }}</mat-error> }
      </mat-form-field>

      <mat-form-field class="full">
        <mat-label>Description</mat-label>
        <textarea matInput rows="4" name="description" [(ngModel)]="description"></textarea>
        @if (form.fieldError('description'); as e) { <mat-error>{{ e }}</mat-error> }
      </mat-form-field>

      <div class="grid">
        <app-search-select label="Type" name="type" [options]="typeOptions" [(ngModel)]="type" />

        <app-search-select label="Urgency" name="urgency" [options]="urgencyOptions"
                           [(ngModel)]="urgency" />

        <mat-form-field>
          <mat-label>Client (optional)</mat-label>
          <input matInput name="clientname" [(ngModel)]="clientName"
                 (ngModelChange)="typed.set($event)" [matAutocomplete]="clients" />
          <mat-autocomplete #clients>
            @for (name of suggestions(); track name) {
              <mat-option [value]="name">{{ name }}</mat-option>
            }
          </mat-autocomplete>
        </mat-form-field>
      </div>

      <mat-form-field class="full">
        <mat-label>What you expected (optional)</mat-label>
        <textarea matInput rows="2" name="expected" [(ngModel)]="expectedResult"></textarea>
      </mat-form-field>

      <mat-form-field class="full">
        <mat-label>What happens instead (optional)</mat-label>
        <textarea matInput rows="2" name="current" [(ngModel)]="currentResult"></textarea>
      </mat-form-field>

      <mat-form-field class="full">
        <mat-label>Steps to reproduce (optional)</mat-label>
        <textarea matInput rows="3" name="steps" [(ngModel)]="reproductionSteps"></textarea>
      </mat-form-field>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [disabled]="!ready() || form.busy()" (click)="save()">
        {{ form.busy() ? 'Saving…' : 'Save changes' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; }
    .grid { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(190px, 1fr)); }
    .note { margin: 0 0 12px; }
    /* The panel is given its width at open(); the content follows it. A min-width larger
       than Material's 560px surface cap is what made this dialog scroll sideways. */
    mat-dialog-content { padding-top: 8px !important; }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class RequestEditDialog implements OnInit {
  readonly ref = inject(MatDialogRef<RequestEditDialog>);
  readonly data = inject<{ request: RequestDetailDto }>(MAT_DIALOG_DATA);
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly form = new FormSubmit();

  readonly types: RequestType[] = [
    'Bug', 'ChangeRequest', 'NewFeature', 'Support', 'Configuration', 'Database', 'Report',
    'Investigation', 'DataCorrection', 'Infrastructure', 'Other',
  ];
  readonly urgencies: RequestedUrgency[] = ['Critical', 'High', 'Normal', 'Low'];

  readonly typeOptions = enumOptions(this.types);
  readonly urgencyOptions = enumOptions(this.urgencies);

  title = this.data.request.title;
  description = this.data.request.description;
  type = this.data.request.type;
  urgency = this.data.request.requestedUrgency;
  clientName = this.data.request.clientName ?? '';
  expectedResult = this.data.request.expectedResult ?? '';
  currentResult = this.data.request.currentResult ?? '';
  reproductionSteps = this.data.request.reproductionSteps ?? '';

  readonly typed = signal(this.clientName);
  private readonly known = signal<string[]>([]);

  readonly suggestions = computed(() => {
    const term = this.typed().trim().toLowerCase();
    const all = this.known();
    return term ? all.filter((n) => n.toLowerCase().includes(term)) : all;
  });

  ngOnInit(): void {
    this.ref.disableClose = true;
    this.api.clients()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((list) => this.known.set(list.map((c) => c.name)));
  }

  ready = () => this.title.trim().length > 0 && this.description.trim().length > 0;

  save(): void {
    if (!this.ready()) return;

    this.form.run(
      (ctx) => this.api.updateRequest(this.data.request.id, {
        title: this.title.trim(),
        description: this.description.trim(),
        type: this.type,
        requestedUrgency: this.urgency,
        clientName: this.clientName.trim(),
        expectedResult: this.expectedResult.trim() || null,
        currentResult: this.currentResult.trim() || null,
        reproductionSteps: this.reproductionSteps.trim() || null,
        businessImpact: this.data.request.businessImpact ?? null,
        targetDate: this.data.request.targetDate ?? null,
      }, ctx),
      (updated) => { this.ref.disableClose = false; this.ref.close(updated); },
    );
  }
}
