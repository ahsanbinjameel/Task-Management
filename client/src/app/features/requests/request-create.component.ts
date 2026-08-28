import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpContext } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ApiService } from '../../core/api.service';
import { ToastService } from '../../core/toast.service';
import { RequestType, RequestedUrgency } from '../../core/models';
import { PageHeaderComponent } from '../../shared/ui';
import { ConfirmDialog, ConfirmData } from '../../shared/dialogs';
import { FileDropComponent } from '../../shared/file-drop.component';
import {
  enumOptions, SearchSelectComponent, SelectOption,
} from '../../shared/search-select.component';

/** One thing being asked for. Text is all it needs; everything else is optional. */
interface Point {
  /** What is wrong, in the requester's own words. The first line becomes the title. */
  text: string;
  files: File[];
  /** Optional detail, revealed by chips and folded into the description on submit. */
  expectedResult: string;
  currentResult: string;
  businessImpact: string;
  reproductionSteps: string;
  shown: Record<string, boolean>;
}

const DETAIL_FIELDS: { key: keyof Point & string; label: string; hint: string }[] = [
  { key: 'expectedResult', label: 'What should happen', hint: 'The total should match the sum of the lines' },
  { key: 'currentResult', label: 'What happens instead', hint: 'It shows the previous month' },
  { key: 'businessImpact', label: 'Why it matters', hint: 'We cannot send invoices until this is right' },
  { key: 'reproductionSteps', label: 'How to see it', hint: 'Open the report, pick August, look at the total' },
];

/**
 * New request: one client, many points, one submit (PRODUCT-CORE §8).
 *
 * This is the highest-value intake screen and the whole target is speed — a submittable request in
 * fifteen or twenty seconds, because the thing it competes with is sending Ahsan a WhatsApp
 * message, and losing that race means the software does not get used.
 *
 * Three things it deliberately does not do.
 *
 * It does not ask the requester to name their submission. The old form demanded a batch title as
 * soon as there was more than one point, which is a question nobody reporting a broken invoice is
 * thinking about; the answer was always a restatement of the first point, so the server now writes
 * that itself.
 *
 * It does not ask for a title *and* a description per point. A point is one piece of text, and its
 * first line is the title. Asking for both means typing the same sentence twice.
 *
 * It does not force the product location. Client is the ceiling for intake, with module and form
 * offered as optional shared defaults for the requester who happens to know; placing the work
 * precisely is a triage concern (§5, §12D) because that is where somebody who knows the codebase
 * is looking at it.
 *
 * There is no single-versus-batch mode. One point posts a plain request, several post a batch, and
 * the person filling the form never learns those are different things.
 */
@Component({
  selector: 'app-request-create',
  standalone: true,
  providers: [provideNativeDateAdapter()],
  imports: [
    FormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    PageHeaderComponent,
    SearchSelectComponent,
    FileDropComponent,
  ],
  template: `
    <div class="page narrow">
      <app-page-header title="New request" />

      <form class="card card-pad stack" (ngSubmit)="submit()">
        <!--
          Shared, and asked once. Every one of these is copied onto each point rather than read
          through the submission, so correcting one item at triage never drags its siblings.
        -->
        <mat-form-field class="full">
          <mat-label>Client</mat-label>
          <input matInput name="client" [(ngModel)]="clientName" [matAutocomplete]="clientList"
                 placeholder="Leave blank for internal work" cdkFocusInitial />
          <mat-autocomplete #clientList>
            @for (name of clientSuggestions(); track name) {
              <mat-option [value]="name">{{ name }}</mat-option>
            }
          </mat-autocomplete>
        </mat-form-field>

        @if (showContext()) {
          <div class="form-grid">
            <app-search-select label="Module (optional)" [options]="moduleOptions()"
                               [ngModel]="moduleId" (ngModelChange)="pickModule($event)"
                               name="module" />
            <app-search-select label="Form (optional)" [options]="formOptions()"
                               [(ngModel)]="formId" name="form" [disabled]="moduleId === null" />
          </div>
        } @else {
          <button type="button" class="add-chip quiet" (click)="showContext.set(true)">
            <mat-icon>add</mat-icon> Say which part of the system, if you know
          </button>
        }

        <!-- --- the points ------------------------------------------------------------------ -->

        <div class="points">
          @for (point of points(); track $index; let i = $index; let last = $last) {
            <div class="point" [class.focused]="focused() === i">
              <div class="point-head">
                <span class="muted small">Point {{ i + 1 }}</span>
                <span class="spacer"></span>
                @if (points().length > 1) {
                  <button matIconButton type="button" (click)="removePoint(i)"
                          [attr.aria-label]="'Remove point ' + (i + 1)">
                    <mat-icon>close</mat-icon>
                  </button>
                }
              </div>

              <mat-form-field class="full">
                <mat-label>What is wrong, or what you need</mat-label>
                <textarea matInput rows="2" [name]="'point' + i"
                          [(ngModel)]="point.text"
                          (focus)="focused.set(i)"
                          placeholder="Delivery order detail report total is not correct"></textarea>
              </mat-form-field>

              <!--
                Optional detail, per point, folded into that point's description on submit. Offered
                as chips and closed by default: most points do not need any of it, and a reviewer
                can ask. Closing a chip clears the field — a value the requester can no longer see
                must never be submitted on their behalf.
              -->
              <div class="row row-wrap chips-row">
                @for (field of detailFields; track field.key) {
                  @if (!point.shown[field.key]) {
                    <button type="button" class="add-chip" (click)="showDetail(point, field.key)">
                      <mat-icon>add</mat-icon> {{ field.label }}
                    </button>
                  }
                }
              </div>

              @for (field of detailFields; track field.key) {
                @if (point.shown[field.key]) {
                  <mat-form-field class="full">
                    <mat-label>{{ field.label }}</mat-label>
                    <textarea matInput rows="2" [name]="field.key + i"
                              [ngModel]="detail(point, field.key)"
                              (ngModelChange)="setDetail(point, field.key, $event)"
                              (focus)="focused.set(i)"
                              [placeholder]="field.hint"></textarea>
                    <button matIconButton matSuffix type="button"
                            (click)="hideDetail(point, field.key)"
                            [attr.aria-label]="'Remove ' + field.label">
                      <mat-icon>close</mat-icon>
                    </button>
                  </mat-form-field>
                }
              }

              <!--
                Per point, not per submission. A screenshot of the broken total belongs to the
                point about the total, and filing all eight against the whole submission is what
                made a reviewer open every one of them to find the relevant picture.

                The active flag is what makes Ctrl+V land here rather than on all of them at once
                — see FileDropComponent.
              -->
              <app-file-drop [(files)]="point.files" [active]="focused() === i" />

              @if (!last) { <hr /> }
            </div>
          }
        </div>

        <div class="row row-wrap">
          <button type="button" class="add-chip" (click)="addPoint()">
            <mat-icon>add</mat-icon> Add another point
          </button>
        </div>

        <!-- --- the rest, folded away ---------------------------------------------------------- -->

        @if (showMore()) {
          <div class="form-grid">
            <app-search-select label="Kind of request" [options]="typeOptions"
                               [(ngModel)]="type" name="type" />
            <app-search-select label="How urgent" [options]="urgencyOptions"
                               [(ngModel)]="urgency" name="urgency" />
            <mat-form-field>
              <mat-label>Needed by (optional)</mat-label>
              <input matInput name="target" [matDatepicker]="picker" [(ngModel)]="targetDate" />
              <mat-datepicker-toggle matIconSuffix [for]="picker" />
              <mat-datepicker #picker />
            </mat-form-field>
          </div>
        } @else {
          <button type="button" class="add-chip quiet" (click)="showMore.set(true)">
            <mat-icon>tune</mat-icon> Kind, urgency and a date
          </button>
        }

        <div class="row">
          <span class="spacer"></span>
          <button matButton type="button" (click)="cancel()">Cancel</button>
          <button matButton="filled" type="submit" [disabled]="!ready() || busy()">
            {{ busy() ? 'Sending…' : submitLabel() }}
          </button>
        </div>
      </form>
    </div>
  `,
  styles: `
    .narrow { max-width: 760px; }
    .full { width: 100%; }

    .points { display: flex; flex-direction: column; }
    .point { padding: 2px 0; }
    .point-head { display: flex; align-items: center; margin-bottom: 2px; }
    .point hr { border: none; border-top: 1px solid var(--border); margin: 16px 0; }

    .chips-row { gap: 8px; align-items: center; margin: -6px 0 8px; }
    .add-chip {
      display: inline-flex; align-items: center; gap: 4px;
      padding: 5px 11px 5px 8px; border-radius: 999px;
      border: 1px dashed var(--border-strong); background: transparent;
      color: var(--text-muted); font-size: 12.5px; cursor: pointer;
    }
    .add-chip:hover { border-style: solid; color: var(--text); background: var(--surface-sunken); }
    .add-chip mat-icon { font-size: 15px; width: 15px; height: 15px; }
    .add-chip.quiet { align-self: flex-start; }

    /* A container that already spaces its children has to null the field's own margin, or the two
       add up and the gap doubles. See the note on form density in CLAUDE.md. */
    .stack > mat-form-field, .point > mat-form-field { margin: 0; }
  `,
})
export class RequestCreateComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly dialog = inject(MatDialog);

  readonly detailFields = DETAIL_FIELDS;

  clientName = '';
  moduleId: number | null = null;
  formId: number | null = null;

  type: RequestType = 'Bug';
  urgency: RequestedUrgency = 'Normal';
  targetDate: Date | null = null;

  readonly typeOptions = enumOptions<RequestType>([
    'Bug', 'ChangeRequest', 'NewFeature', 'Support', 'Configuration',
    'Database', 'Report', 'Investigation', 'DataCorrection', 'Infrastructure', 'Other',
  ]);

  readonly urgencyOptions = enumOptions<RequestedUrgency>(['Low', 'Normal', 'High', 'Critical']);

  readonly points = signal<Point[]>([blankPoint()]);
  /** Which point the reader is in, so a pasted screenshot lands on that one. */
  readonly focused = signal(0);

  readonly showContext = signal(false);
  readonly showMore = signal(false);
  readonly busy = signal(false);

  readonly clientSuggestions = signal<string[]>([]);
  readonly moduleOptions = signal<SelectOption[]>([]);
  readonly formOptions = signal<SelectOption[]>([]);

  ngOnInit(): void {
    this.api.clients().subscribe({
      next: (clients) => this.clientSuggestions.set(clients.map((c) => c.name)),
      error: () => undefined,
    });
    this.api.modules().subscribe({
      next: (modules) => this.moduleOptions.set(modules.map((m) => ({ value: m.id, label: m.name }))),
      error: () => undefined,
    });
  }

  /** A form belongs to a module, so changing the module drops a form that no longer fits. */
  pickModule(moduleId: number | null): void {
    this.moduleId = moduleId;
    this.formId = null;
    this.formOptions.set([]);
    if (moduleId === null) return;

    this.api.formOptions(moduleId).subscribe({
      next: (forms) => this.formOptions.set(forms.map((f) => ({ value: f.id, label: f.name }))),
      error: () => undefined,
    });
  }

  // --- the points -----------------------------------------------------------------------------

  addPoint(): void {
    this.points.update((all) => [...all, blankPoint()]);
    this.focused.set(this.points().length - 1);
  }

  removePoint(index: number): void {
    this.points.update((all) => all.filter((_, i) => i !== index));
    this.focused.set(Math.min(this.focused(), this.points().length - 1));
  }

  detail(point: Point, key: string): string {
    return (point as unknown as Record<string, string>)[key] ?? '';
  }

  setDetail(point: Point, key: string, value: string): void {
    (point as unknown as Record<string, string>)[key] = value;
  }

  showDetail(point: Point, key: string): void {
    point.shown[key] = true;
  }

  /** Clearing on close is deliberate: never submit a value the requester can no longer see. */
  hideDetail(point: Point, key: string): void {
    point.shown[key] = false;
    this.setDetail(point, key, '');
  }

  // --- submitting -----------------------------------------------------------------------------

  private filled(): Point[] {
    return this.points().filter((p) => p.text.trim().length > 0);
  }

  readonly ready = computed(() => this.points().some((p) => p.text.trim().length > 0));

  submitLabel(): string {
    const count = this.filled().length;
    return count > 1 ? `Submit ${count} points` : 'Submit request';
  }

  cancel(): void {
    void this.router.navigate(['/requests']);
  }

  submit(): void {
    const points = this.filled();
    if (points.length === 0 || this.busy()) return;

    // Submitting is a commitment other people act on, so it is confirmed. This one returns a plain
    // true rather than performing the call inside the dialog: it is a whole page that survives a
    // refusal untouched, and the submit path goes on to upload attachments afterwards — work that
    // cannot run inside a dialog that has already closed.
    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: {
          title: points.length > 1 ? `Submit these ${points.length} points?` : 'Submit this request?',
          message:
            'It goes to a reviewer, who decides what happens next. You can follow it from the '
            + 'Requests page without having to ask anyone.',
          confirmText: 'Submit',
        },
      })
      .afterClosed()
      .subscribe((confirmed?: boolean) => {
        if (confirmed) this.send(points);
      });
  }

  private send(points: Point[]): void {
    this.busy.set(true);

    // One point posts a plain request, several post a batch. The person filling the form never
    // learns those are different things — but a batch row for every single request would put a
    // "submission" wrapper around work that never came with anything else, and CLAUDE.md keeps
    // that null for the ordinary case.
    if (points.length === 1) {
      this.sendOne(points[0]);
      return;
    }

    this.api
      .createBatch({
        clientName: this.clientName.trim() || null,
        moduleId: this.moduleId ?? undefined,
        formId: this.formId ?? undefined,
        items: points.map((p) => ({
          title: titleOf(p.text),
          description: describe(p),
          type: this.type,
          requestedUrgency: this.urgency,
          targetDate: this.targetDate ? this.targetDate.toISOString() : null,
        })),
      })
      .subscribe({
        next: (batch) => {
          // Each point's files against that point's own request, in the order they were typed.
          const uploads = batch.items.map((item, i) => ({ id: item.id, files: points[i]?.files ?? [] }));
          this.uploadAll(uploads, () => {
            this.toast.success(`${batch.items.length} requests submitted.`);
            void this.router.navigate(['/requests/batches', batch.id]);
          });
        },
        error: () => this.busy.set(false),
      });
  }

  private sendOne(point: Point): void {
    this.api
      .createRequest({
        title: titleOf(point.text),
        description: describe(point),
        type: this.type,
        requestedUrgency: this.urgency,
        clientName: this.clientName.trim() || null,
        moduleId: this.moduleId ?? undefined,
        formId: this.formId ?? undefined,
        targetDate: this.targetDate ? this.targetDate.toISOString() : null,
      })
      .subscribe({
        next: (request) => {
          this.uploadAll([{ id: request.id, files: point.files }], () => {
            this.toast.success(`${request.requestNumber} submitted.`);
            void this.router.navigate(['/requests', request.id]);
          });
        },
        error: () => this.busy.set(false),
      });
  }

  /**
   * Files go up after the records exist, because an attachment needs something to hang off.
   *
   * A failed upload does not fail the submission: the request is already saved and telling someone
   * their whole submission was lost because one screenshot did not upload would be a lie. They are
   * told the picture is missing and can add it on the request itself.
   */
  private uploadAll(targets: { id: number; files: File[] }[], done: () => void): void {
    const jobs = targets.flatMap((t) => t.files.map((file) => ({ id: t.id, file })));
    if (jobs.length === 0) {
      this.busy.set(false);
      done();
      return;
    }

    let remaining = jobs.length;
    let failed = 0;

    const finish = () => {
      if (--remaining > 0) return;
      this.busy.set(false);
      if (failed > 0) {
        this.toast.error(
          failed === 1
            ? 'One attachment did not upload. You can add it on the request.'
            : `${failed} attachments did not upload. You can add them on the request.`);
      }
      done();
    };

    for (const job of jobs) {
      this.api.uploadRequestAttachment(job.id, job.file).subscribe({
        next: finish,
        error: () => { failed++; finish(); },
      });
    }
  }
}

function blankPoint(): Point {
  return {
    text: '',
    files: [],
    expectedResult: '',
    currentResult: '',
    businessImpact: '',
    reproductionSteps: '',
    shown: {},
  };
}

/**
 * The first line, which is what somebody skimming a queue reads.
 *
 * A point is one piece of text, so there is no separate title to ask for — asking for both means
 * typing the same sentence twice, and that is most of what made the old form slow.
 */
function titleOf(text: string): string {
  const first = text.trim().split('\n')[0].trim();
  return first.length <= 300 ? first : `${first.slice(0, 299).trimEnd()}…`;
}

/** The point in full, with whatever optional detail was filled in folded underneath it. */
function describe(point: Point): string {
  const parts = [point.text.trim()];

  for (const field of DETAIL_FIELDS) {
    const value = (point as unknown as Record<string, string>)[field.key]?.trim();
    if (value) parts.push(`${field.label}: ${value}`);
  }

  return parts.join('\n\n');
}
