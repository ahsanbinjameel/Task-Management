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
import { MatDialog } from '@angular/material/dialog';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { RequestType, RequestedUrgency } from '../../core/models';
import { enumOptions, SearchSelectComponent } from '../../shared/search-select.component';
import { PageHeaderComponent } from '../../shared/ui';
import { FileDropComponent } from '../../shared/file-drop.component';
import { ConfirmDialog, ConfirmData } from '../../shared/dialogs';

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
        @if (extras.length > 0) {
          <mat-form-field class="full">
            <mat-label>What is this batch about?</mat-label>
            <input matInput formControlName="batchTitle"
                   placeholder="e.g. Month-end problems" />
          </mat-form-field>

          <div class="item-head first">
            <span class="muted small">Asking for &mdash; 1</span>
          </div>
        }

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
          @if (extras.length > 0) {
            <p class="muted small items-note">
              The extra detail fields below belong to this first request. The others ask for a title
              and a description &mdash; a reviewer can always come back with a question.
            </p>
          }
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

        <!--
          Asking for more than one thing.
          Hidden until it is wanted, because most requests are one thing and a repeatable list on
          an empty form makes a simple job look like an ordering system. Once open, each extra item
          asks only for what an item needs: the client, the files and the reviewer are shared, so
          nobody retypes them.
        -->
        @if (extras.length > 0) {
          <div class="items" formArrayName="extras">
            <p class="muted small items-note">
              These go in together as {{ extras.length + 1 }} separate requests, sharing the client
              and files above. A reviewer decides on each one, and may combine any of them into a
              single piece of work.
            </p>

            @for (item of extras.controls; track $index; let i = $index) {
              <div class="item card-pad" [formGroupName]="i">
                <div class="row item-head">
                  <span class="muted small">Also asking for &mdash; {{ i + 2 }}</span>
                  <span class="spacer"></span>
                  <button matIconButton type="button" (click)="removeItem(i)"
                          [attr.aria-label]="'Remove item ' + (i + 2)">
                    <mat-icon>close</mat-icon>
                  </button>
                </div>

                <mat-form-field class="full">
                  <mat-label>Title</mat-label>
                  <input matInput formControlName="title" placeholder="Short summary" />
                </mat-form-field>

                <mat-form-field class="full">
                  <mat-label>Description</mat-label>
                  <textarea matInput rows="3" formControlName="description"
                            placeholder="What do you need, and where does it happen?"></textarea>
                </mat-form-field>

                <div class="form-grid">
                  <app-search-select label="Type" [options]="typeOptions" formControlName="type" />
                  <app-search-select label="Urgency" [options]="urgencyOptions"
                                     formControlName="requestedUrgency" />
                </div>
              </div>
            }
          </div>
        }

        <div class="row row-wrap">
          <button type="button" class="add-chip" (click)="addItem()">
            <mat-icon>add</mat-icon>
            {{ extras.length === 0 ? 'Ask for something else too' : 'Add another' }}
          </button>
        </div>

        <div class="attach">
          <h2 class="card-title">
            Attachments
            @if (extras.length > 0) { <span class="muted small">&mdash; shared by every item</span> }
          </h2>
          <app-file-drop [(files)]="files" />
        </div>

        <div class="row">
          <span class="spacer"></span>
          <button matButton type="button" (click)="cancel()">Cancel</button>
          <button matButton="filled" type="submit" [disabled]="form.invalid || busy()">
            {{ busy() ? 'Sending…' : extras.length === 0
                ? 'Submit request'
                : 'Submit ' + (extras.length + 1) + ' requests' }}
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
    .items { display: flex; flex-direction: column; gap: 12px; margin: 6px 0 12px; }
    .items-note { margin: 0 0 10px; }
    .item {
      border: 1px solid var(--border); border-radius: var(--radius);
      background: var(--surface-sunken);
    }
    .item-head { align-items: center; margin-bottom: 6px; }
    .item-head.first { margin: 4px 0 -4px; }
    .attach { margin: 6px 0 18px; }
    .attach .card-title { margin-bottom: 10px; }
  `,
})
export class RequestCreateComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly dialog = inject(MatDialog);

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

  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    /** Only used, and only required, once there is more than one item. */
    batchTitle: [''],
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
    extras: this.fb.array([] as ReturnType<RequestCreateComponent['newItem']>[]),
  });

  get extras() {
    return this.form.controls.extras;
  }

  private newItem() {
    return this.fb.nonNullable.group({
      title: ['', [Validators.required, Validators.maxLength(300)]],
      description: ['', [Validators.required, Validators.maxLength(8000)]],
      type: ['Support' as RequestType, Validators.required],
      requestedUrgency: ['Normal' as RequestedUrgency, Validators.required],
    });
  }

  addItem(): void {
    // The batch needs a name of its own once there is more than one item — the items have their
    // own titles, and "Month-end problems" is what a reviewer scans a queue for.
    if (this.extras.length === 0) {
      this.form.controls.batchTitle.addValidators([Validators.required, Validators.maxLength(300)]);
      this.form.controls.batchTitle.updateValueAndValidity();

      if (!this.form.controls.batchTitle.value.trim()) {
        this.form.controls.batchTitle.setValue(this.form.controls.title.value.trim());
      }
    }

    this.extras.push(this.newItem());
  }

  removeItem(index: number): void {
    this.extras.removeAt(index);

    // Back to one request: the batch title stops being required, and stops being sent.
    if (this.extras.length === 0) {
      this.form.controls.batchTitle.clearValidators();
      this.form.controls.batchTitle.updateValueAndValidity();
    }
  }

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

  /**
   * Asks before submitting, then hands off to the real thing.
   *
   * Unlike the dialogs elsewhere this one only returns an answer rather than performing the call:
   * the form is a whole page that survives a refusal untouched, and the submit path continues on
   * afterwards to upload the attachments — work that has no business running inside a dialog that
   * has already closed.
   */
  submit(): void {
    if (this.form.invalid) return;

    const extras = this.form.getRawValue().extras.length;

    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: {
          title: extras > 0 ? `Submit these ${extras + 1} requests?` : 'Submit this request?',
          message: extras > 0
            ? `All ${extras + 1} go to the review queue together under one submission. You can `
              + 'still edit each one until a reviewer picks it up.'
            : 'It goes to the review queue for someone to look at. You can still edit it until a '
              + 'reviewer picks it up.',
          confirmText: extras > 0 ? 'Submit them' : 'Submit it',
        },
      })
      .afterClosed()
      .subscribe((confirmed?: boolean) => {
        if (confirmed) this.send();
      });
  }

  private send(): void {
    this.busy.set(true);

    const v = this.form.getRawValue();

    if (v.extras.length > 0) {
      this.submitBatch(v);
      return;
    }

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

  /**
   * Several at once. The first item is the main form; the extras follow it in the order they were
   * added. Files go up afterwards and against the *batch*, not against an item — the screenshot
   * showing all eight problems belongs to the submission.
   */
  private submitBatch(v: ReturnType<typeof this.form.getRawValue>): void {
    this.api
      .createBatch({
        title: v.batchTitle.trim() || v.title.trim(),
        clientName: v.clientName.trim() || undefined,
        items: [
          {
            title: v.title.trim(),
            description: this.withDetail(v),
            type: v.type,
            requestedUrgency: v.requestedUrgency,
            targetDate: v.targetDate ? v.targetDate.toISOString() : null,
          },
          ...v.extras.map((e) => ({
            title: e.title.trim(),
            description: e.description.trim(),
            type: e.type,
            requestedUrgency: e.requestedUrgency,
            targetDate: null,
          })),
        ],
      })
      .subscribe({
        next: (batch) => {
          this.uploadBatchFiles(batch.id, () => {
            this.busy.set(false);
            this.toast.success(`${batch.batchNumber}: ${batch.items.length} requests submitted.`);
            void this.router.navigate(['/requests/batches', batch.id]);
          });
        },
        error: () => this.busy.set(false),
      });
  }

  /**
   * A batch item carries a title and a description, so the first item's optional detail is folded
   * into its description under its own heading rather than dropped. Nothing the requester typed is
   * discarded, and nothing is sent that they cannot see: the labels are the same words the fields
   * carried.
   */
  private withDetail(v: ReturnType<typeof this.form.getRawValue>): string {
    const parts = [v.description.trim()];

    for (const field of OPTIONAL_FIELDS) {
      const value = (v[field.key] ?? '').trim();
      if (value) parts.push(`${field.label}:\n${value}`);
    }

    return parts.join('\n\n');
  }

  private uploadBatchFiles(batchId: number, done: () => void): void {
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
      this.api.uploadBatchAttachment(batchId, file).subscribe({
        next: finish,
        error: () => {
          this.toast.error(`${file.name} could not be attached.`);
          finish();
        },
      });
    }
  }

  cancel(): void {
    void this.router.navigate(['/requests']);
  }
}
