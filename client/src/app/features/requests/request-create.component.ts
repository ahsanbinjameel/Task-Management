import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { RequestType, RequestedUrgency } from '../../core/models';
import { enumOptions, SearchSelectComponent } from '../../shared/search-select.component';
import { PageHeaderComponent } from '../../shared/ui';
import { FileDropComponent } from '../../shared/file-drop.component';

/** The optional detail fields, in the order they are offered. */
const OPTIONAL_FIELDS = [
  {
    key: 'expectedResult' as const,
    label: 'What should happen',
    hint: 'What did you expect to see?',
    rows: 2,
  },
  {
    key: 'currentResult' as const,
    label: 'What happens instead',
    hint: 'What actually happens?',
    rows: 2,
  },
  {
    key: 'reproductionSteps' as const,
    label: 'Steps to reproduce',
    hint: 'One step per line, so someone else can see it too.',
    rows: 3,
  },
  {
    key: 'businessImpact' as const,
    label: 'Why it matters',
    hint: 'What does it cost while this is not done?',
    rows: 2,
  },
];

type OptionalKey = (typeof OPTIONAL_FIELDS)[number]['key'];

/**
 * Which details are worth asking for, by type.
 *
 * A bug is a report about a difference between what happened and what should have: those two
 * fields plus the steps are the whole of it. A change is a description of a wanted outcome and a
 * reason for wanting it. Support is a question — asking someone with a question for reproduction
 * steps is how you teach people not to ask.
 */
const SUGGESTED_BY_TYPE: Record<string, OptionalKey[]> = {
  Bug: ['expectedResult', 'currentResult', 'reproductionSteps', 'businessImpact'],
  DataCorrection: ['expectedResult', 'currentResult', 'businessImpact'],
  ChangeRequest: ['expectedResult', 'businessImpact'],
  NewFeature: ['expectedResult', 'businessImpact'],
  Report: ['expectedResult', 'businessImpact'],
  Configuration: ['expectedResult'],
  Database: ['expectedResult', 'currentResult'],
  Investigation: ['currentResult', 'businessImpact'],
  Infrastructure: ['currentResult', 'businessImpact'],
  Support: [],
  Other: [],
};

@Component({
  selector: 'app-request-create',
  standalone: true,
  providers: [provideNativeDateAdapter()],
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatAutocompleteModule,
    PageHeaderComponent,
    SearchSelectComponent,
    FileDropComponent,
  ],
  template: `
    <div class="page narrow">
      <app-page-header
        title="New request"
        subtitle="Tell us what you need. A reviewer decides what happens next."
      />

      <form class="card card-pad stack" [formGroup]="form" (ngSubmit)="submit()">
        <mat-form-field class="full">
          <mat-label>Title</mat-label>
          <input matInput formControlName="title" placeholder="Short summary of what you need" />
        </mat-form-field>

        <mat-form-field class="full">
          <mat-label>Description</mat-label>
          <textarea matInput rows="4" formControlName="description"
                    placeholder="What do you need, and where does it happen?"></textarea>
        </mat-form-field>

        <div class="form-grid">
          <mat-form-field>
            <mat-label>Client (optional)</mat-label>
            <input matInput formControlName="clientName" [matAutocomplete]="clientList"
                   placeholder="Type a name, or leave blank" />
            <mat-autocomplete #clientList>
              @for (name of suggestions(); track name) {
                <mat-option [value]="name">{{ name }}</mat-option>
              }
            </mat-autocomplete>
          </mat-form-field>

          <app-search-select label="Type" [options]="typeOptions" formControlName="type" />

          <app-search-select label="Urgency" [options]="urgencyOptions"
                             formControlName="requestedUrgency" />

          <mat-form-field>
            <mat-label>Needed by (optional)</mat-label>
            <input matInput [matDatepicker]="picker" formControlName="targetDate" />
            <mat-datepicker-toggle matIconSuffix [for]="picker" />
            <mat-datepicker #picker />
          </mat-form-field>
        </div>

        <!--
          The optional detail. Folded away by default and offered as chips, because most requests
          do not need any of it: "the payroll report errors when I generate it" is a perfectly good
          request, and the reviewer can ask for more if they need it. Making people fill in a
          technical bug-analysis form before they can report anything is how you stop them
          reporting things.

          Which chips are offered follows the type — nobody asks a support request for steps to
          reproduce — but every chip stays available, because the type is a guess and the requester
          knows what they have to say.
        -->
        <div class="optional">
          <div class="row row-wrap chips-row">
            <span class="muted small label">Add more detail (optional)</span>
            @for (field of optionalFields; track field.key) {
              @if (!shown()[field.key]) {
                <button type="button" class="add-chip"
                        [class.suggested]="suggested().includes(field.key)"
                        (click)="show(field.key)">
                  <mat-icon>add</mat-icon> {{ field.label }}
                </button>
              }
            }
          </div>

          @for (field of optionalFields; track field.key) {
            @if (shown()[field.key]) {
              <mat-form-field class="full">
                <mat-label>{{ field.label }}</mat-label>
                <textarea matInput [rows]="field.rows" [formControlName]="field.key"
                          [placeholder]="field.hint"></textarea>
                <button matIconButton matSuffix type="button" (click)="hide(field.key)"
                        [attr.aria-label]="'Remove ' + field.label">
                  <mat-icon>close</mat-icon>
                </button>
              </mat-form-field>
            }
          }
        </div>

        <div class="attach">
          <h2 class="card-title">Attachments</h2>
          <app-file-drop [(files)]="files" />
        </div>

        <div class="row">
          <span class="spacer"></span>
          <button matButton type="button" (click)="cancel()">Cancel</button>
          <button matButton="filled" type="submit" [disabled]="form.invalid || busy()">
            {{ busy() ? 'Sending…' : 'Submit request' }}
          </button>
        </div>
      </form>
    </div>
  `,
  styles: `
    .narrow { max-width: 880px; }
    .full { width: 100%; }
    .optional { margin: 2px 0 10px; }
    .chips-row { gap: 8px; align-items: center; margin-bottom: 12px; }
    .chips-row .label { margin-right: 2px; }
    .add-chip {
      display: inline-flex; align-items: center; gap: 4px;
      border: 1px dashed var(--border-strong); background: transparent;
      border-radius: 999px; padding: 5px 12px 5px 8px;
      font: inherit; font-size: 12.5px; color: var(--text-muted); cursor: pointer;
    }
    .add-chip:hover { border-style: solid; color: inherit; background: var(--surface-sunken); }
    /* The ones that fit the chosen type, nudged forward without being forced on anyone. */
    .add-chip.suggested { border-style: solid; color: inherit; }
    .add-chip mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .attach { margin: 6px 0 18px; }
    .attach .card-title { margin-bottom: 10px; }
  `,
})
export class RequestCreateComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  private readonly destroyRef = inject(DestroyRef);
  readonly busy = signal(false);

  readonly types: RequestType[] = [
    'Bug',
    'ChangeRequest',
    'NewFeature',
    'Support',
    'Configuration',
    'Database',
    'Report',
    'Investigation',
    'DataCorrection',
    'Infrastructure',
    'Other',
  ];
  readonly urgencies: RequestedUrgency[] = ['Critical', 'High', 'Normal', 'Low'];

  readonly typeOptions = enumOptions(this.types);
  readonly urgencyOptions = enumOptions(this.urgencies);

  readonly optionalFields = OPTIONAL_FIELDS;

  /** Which optional fields are open. Closing one hides it; only the × clears what was typed. */
  readonly shown = signal<Record<OptionalKey, boolean>>({
    expectedResult: false,
    currentResult: false,
    reproductionSteps: false,
    businessImpact: false,
  });

  /** The fields worth offering for the chosen type. A hint, not a restriction. */
  readonly suggested = signal<OptionalKey[]>(SUGGESTED_BY_TYPE['Support']);

  show(key: OptionalKey): void {
    this.shown.update((all) => ({ ...all, [key]: true }));
  }

  /**
   * Hiding a field also clears it. A value the requester can no longer see must not be submitted
   * on their behalf — that is the one thing worse than losing it.
   */
  hide(key: OptionalKey): void {
    this.form.controls[key].setValue('');
    this.shown.update((all) => ({ ...all, [key]: false }));
  }

  readonly form = inject(FormBuilder).nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(300)]],
    description: ['', [Validators.required, Validators.maxLength(8000)]],
    type: ['Support' as RequestType, Validators.required],
    requestedUrgency: ['Normal' as RequestedUrgency, Validators.required],
    targetDate: [null as Date | null],
    clientName: [''],
    businessImpact: [''],
    expectedResult: [''],
    currentResult: [''],
    reproductionSteps: [''],
  });

  /**
   * Names already in use. Loaded once and filtered locally: the list is short by nature, and a
   * request per keystroke would be a lot of traffic for a field most people leave alone.
   */
  private readonly known = signal<string[]>([]);

  readonly suggestions = computed(() => {
    const typed = (this.typed() ?? '').trim().toLowerCase();
    const all = this.known();
    return typed ? all.filter((n) => n.toLowerCase().includes(typed)) : all;
  });

  private readonly typed = signal<string>('');

  ngOnInit(): void {
    this.api.clients().subscribe((list) => this.known.set(list.map((c) => c.name)));

    this.form.controls.clientName.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => this.typed.set(value ?? ''));

    // The type changes which details are worth asking for. Nothing already opened is closed —
    // the requester's decision outranks the guess.
    this.form.controls.type.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((type) => this.suggested.set(SUGGESTED_BY_TYPE[type] ?? []));
  }

  /** Owned here, filled in by the drop zone — see `app-file-drop`. */
  readonly files = signal<File[]>([]);

  /** Uploads sequentially, then continues regardless — see the note at the call site. */
  private uploadPending(requestId: number, done: () => void): void {
    const queue = this.files();
    if (queue.length === 0) {
      done();
      return;
    }

    let remaining = queue.length;
    const finish = () => {
      if (--remaining === 0) done();
    };

    for (const file of queue) {
      this.api.uploadRequestAttachment(requestId, file).subscribe({
        next: finish,
        error: () => {
          this.toast.error(`${file.name} could not be attached.`);
          finish();
        },
      });
    }
  }

  submit(): void {
    if (this.form.invalid) return;
    this.busy.set(true);

    const v = this.form.getRawValue();

    this.api
      .createRequest({
        title: v.title.trim(),
        description: v.description.trim(),
        type: v.type,
        requestedUrgency: v.requestedUrgency,
        targetDate: v.targetDate ? v.targetDate.toISOString() : null,
        businessImpact: v.businessImpact.trim() || undefined,
        expectedResult: v.expectedResult.trim() || undefined,
        currentResult: v.currentResult.trim() || undefined,
        reproductionSteps: v.reproductionSteps.trim() || undefined,
        clientName: v.clientName.trim() || undefined,
      })
      .subscribe({
        next: (created) => {
          // Files can only be attached once the request exists, so they follow it rather than
          // going up with it. The request is already saved either way — a failed upload must not
          // look like a failed submission.
          this.uploadPending(created.id, () => {
            this.busy.set(false);
            this.toast.success(`${created.requestNumber} submitted.`);
            void this.router.navigate(['/requests', created.id]);
          });
        },
        error: () => this.busy.set(false),
      });
  }

  cancel(): void {
    void this.router.navigate(['/requests']);
  }
}
