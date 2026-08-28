import { Component, OnInit, computed, inject, signal } from '@angular/core';
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
import { RequestedUrgency } from '../../core/models';
import { PageHeaderComponent } from '../../shared/ui';
import { BackLinkComponent } from '../../shared/back-link.component';
import { ConfirmDialog, ConfirmData } from '../../shared/dialogs';
import { FileDropComponent } from '../../shared/file-drop.component';
import { enumOptions, SearchSelectComponent } from '../../shared/search-select.component';

/** One thing being asked for. Only the first line is required. */
interface Point {
  text: string;
  description: string;
  urgency: RequestedUrgency;
  neededBy: Date | null;
  files: File[];
  expectedResult: string;
  currentResult: string;
  businessImpact: string;
  reproductionSteps: string;
  shown: Record<string, boolean>;
  /** Progressive sections, opened per point rather than for the whole form. */
  open: { description: boolean; files: boolean };
}

const DETAIL_FIELDS: { key: string; label: string }[] = [
  { key: 'expectedResult', label: 'What should happen' },
  { key: 'currentResult', label: 'What happens instead' },
  { key: 'reproductionSteps', label: 'Steps to reproduce' },
  { key: 'businessImpact', label: 'Why it matters' },
];

/**
 * New request: one client, many points, one submit.
 *
 * Density is the whole point of this screen. It competes with sending a WhatsApp message, and a
 * form that runs off the bottom of a 1366×768 laptop after two points loses that race — so related
 * fields share a row, and everything not needed to submit is folded away until it is asked for.
 *
 * What it does not ask for, and why.
 *
 * **Type.** The requester knows what is wrong, not whether it is a defect, a change request or a
 * configuration mistake. A guess from them is a wrong label on every report that groups by it, so
 * the reviewer sets it at triage.
 *
 * **A title separate from a description.** A point is one piece of text and its first line is the
 * title. Asking for both means typing the same sentence twice; the longer version is offered for
 * people who have more to say.
 *
 * **Urgency and needed-by belong to the point, not the submission.** Somebody reporting three
 * things usually needs one today and the others whenever. One setting for all three would be wrong
 * for two of them, and the requester would have to send three separate submissions to say so.
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
    BackLinkComponent,
    SearchSelectComponent,
    FileDropComponent,
  ],
  template: `
    <div class="page narrow">
      <app-back-link fallback="/requests" label="Requests" />
      <app-page-header title="New request" />

      <form (ngSubmit)="submit()">
        <!--
          Asked once and copied onto each item by the API, so an item corrected at triage never
          drags its siblings. It sits above the points because it is the one thing that is genuinely
          shared.
        -->
        <mat-form-field class="client card">
          <mat-label>Client</mat-label>
          <input matInput name="client" [(ngModel)]="clientName" [matAutocomplete]="clientList" />
          <mat-autocomplete #clientList>
            @for (name of clientSuggestions(); track name) {
              <mat-option [value]="name">{{ name }}</mat-option>
            }
          </mat-autocomplete>
        </mat-form-field>

        @for (point of points(); track $index; let i = $index) {
          <section class="card card-pad item">
            <header class="item-head">
              <span class="tag">Request {{ i + 1 }}</span>
              <span class="spacer"></span>
              @if (points().length > 1) {
                <button matButton type="button" class="remove" (click)="removePoint(i)">
                  Remove
                </button>
              }
            </header>

            <!--
              What is needed, and the two small facts about it, on one row on desktop. Neither
              urgency nor a date is worth a row of its own, and stacking them is what turned a
              three-point submission into a page of scrolling.
            -->
            <div class="row-3">
              <mat-form-field class="ask">
                <mat-label>What do you need?</mat-label>
                <textarea
                  matInput
                  rows="2"
                  [name]="'text' + i"
                  [(ngModel)]="point.text"
                  (focus)="focused.set(i)"
                ></textarea>
              </mat-form-field>

              <app-search-select
                label="Requested urgency"
                [options]="urgencyOptions"
                [(ngModel)]="point.urgency"
                [name]="'urgency' + i"
              />

              <mat-form-field>
                <mat-label>Needed by</mat-label>
                <input
                  matInput
                  [name]="'needed' + i"
                  [matDatepicker]="picker"
                  [(ngModel)]="point.neededBy"
                />
                <mat-datepicker-toggle matIconSuffix [for]="picker" />
                <mat-datepicker #picker />
              </mat-form-field>
            </div>

            @if (point.open.description) {
              <mat-form-field class="full">
                <mat-label>Description</mat-label>
                <textarea
                  matInput
                  rows="2"
                  [name]="'desc' + i"
                  [(ngModel)]="point.description"
                  (focus)="focused.set(i)"
                ></textarea>
              </mat-form-field>
            }

            @for (field of detailFields; track field.key) {
              @if (point.shown[field.key]) {
                <mat-form-field class="full">
                  <mat-label>{{ field.label }}</mat-label>
                  <textarea
                    matInput
                    rows="2"
                    [name]="field.key + i"
                    [ngModel]="detail(point, field.key)"
                    (ngModelChange)="setDetail(point, field.key, $event)"
                    (focus)="focused.set(i)"
                  ></textarea>
                  <button
                    matIconButton
                    matSuffix
                    type="button"
                    (click)="hideDetail(point, field.key)"
                    [attr.aria-label]="'Remove ' + field.label"
                  >
                    <mat-icon>close</mat-icon>
                  </button>
                </mat-form-field>
              }
            }

            @if (point.open.files) {
              <!-- Per point: a screenshot of the broken total belongs to the point about it. -->
              <app-file-drop [(files)]="point.files" [active]="focused() === i" />
            }

            <!--
              Everything optional is one row of chips until it is wanted. Closed, this costs a
              single line; open, only the parts actually in use take space.
            -->
            <div class="chips">
              @if (!point.open.description) {
                <button type="button" class="chip-add" (click)="point.open.description = true">
                  <mat-icon>add</mat-icon> Description
                </button>
              }
              @for (field of detailFields; track field.key) {
                @if (!point.shown[field.key]) {
                  <button type="button" class="chip-add" (click)="point.shown[field.key] = true">
                    <mat-icon>add</mat-icon> {{ field.label }}
                  </button>
                }
              }
              @if (!point.open.files) {
                <button type="button" class="chip-add" (click)="openFiles(point, i)">
                  <mat-icon>attach_file</mat-icon> Attachment
                </button>
              }
            </div>
          </section>
        }

        <div class="actions">
          <button type="button" class="chip-add" (click)="addPoint()">
            <mat-icon>add</mat-icon> Add another request
          </button>
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
    .narrow {
      max-width: 940px;
    }
    .full {
      width: 100%;
    }
    .client {
      width: min(360px, 100%);
      margin-bottom: 12px;
    }
    .ask {
      grid-column: 1/-1;
    }

    .item {
      margin-bottom: 12px;
      padding-top: 10px;
    }
    .item-head {
      display: flex;
      align-items: center;
      margin-bottom: 12px;
    }
    .tag {
      font-size: 11px;
      font-weight: 700;
      letter-spacing: 0.07em;
      text-transform: uppercase;
      color: var(--text-muted);
    }
    .remove {
      color: var(--text-muted);
      min-width: 0;
    }

    /* Three across on desktop; stacked once they would stop being readable. */
    .row-3 {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 12px;
      align-items: start;
    }
    @media (max-width: 820px) {
      .row-3 {
        grid-template-columns: 1fr;
      }
    }
    .row-3 mat-form-field,
    .row-3 app-search-select {
      width: 100%;
    }

    .chips {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      margin-top: 4px;
    }
    .chip-add {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 4px 10px 4px 7px;
      border-radius: 999px;
      border: 1px dashed var(--border-strong);
      background: transparent;
      color: var(--text-muted);
      font-size: 12.5px;
      cursor: pointer;
    }
    .chip-add:hover {
      border-style: solid;
      color: var(--text);
      background: var(--surface-sunken);
    }
    .chip-add mat-icon {
      font-size: 15px;
      width: 15px;
      height: 15px;
    }

    .actions {
      display: flex;
      align-items: center;
      gap: 8px;
      flex-wrap: wrap;
      margin-top: 4px;
    }
    .spacer {
      flex: 1 1 auto;
    }
  `,
})
export class RequestCreateComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly dialog = inject(MatDialog);

  readonly detailFields = DETAIL_FIELDS;

  /** Shared across the submission and copied onto each item, as the API already does. */
  clientName = '';

  readonly urgencyOptions = enumOptions<RequestedUrgency>(['Low', 'Normal', 'High', 'Critical']);

  readonly points = signal<Point[]>([blankPoint()]);
  /** Which point is being typed into, so a pasted screenshot lands on that one. */
  readonly focused = signal(0);
  readonly busy = signal(false);
  readonly clientSuggestions = signal<string[]>([]);

  ngOnInit(): void {
    this.api.clients().subscribe({
      next: (clients) => this.clientSuggestions.set(clients.map((c) => c.name)),
      error: () => undefined,
    });
  }

  addPoint(): void {
    this.points.update((all) => [...all, blankPoint()]);
    this.focused.set(this.points().length - 1);
  }

  removePoint(index: number): void {
    this.points.update((all) => all.filter((_, i) => i !== index));
    this.focused.set(Math.min(this.focused(), this.points().length - 1));
  }

  openFiles(point: Point, index: number): void {
    point.open.files = true;
    this.focused.set(index);
  }

  detail(point: Point, key: string): string {
    return (point as unknown as Record<string, string>)[key] ?? '';
  }

  setDetail(point: Point, key: string, value: string): void {
    (point as unknown as Record<string, string>)[key] = value;
  }

  /** Clearing on close is deliberate: never submit a value the requester can no longer see. */
  hideDetail(point: Point, key: string): void {
    point.shown[key] = false;
    this.setDetail(point, key, '');
  }

  private filled(): Point[] {
    return this.points().filter((p) => p.text.trim().length > 0);
  }

  readonly ready = computed(() => this.points().some((p) => p.text.trim().length > 0));

  submitLabel(): string {
    const count = this.filled().length;
    return count > 1 ? `Submit ${count} requests` : 'Submit request';
  }

  cancel(): void {
    void this.router.navigate(['/requests']);
  }

  submit(): void {
    const points = this.filled();
    if (points.length === 0 || this.busy()) return;

    this.dialog
      .open<ConfirmDialog, ConfirmData>(ConfirmDialog, {
        data: {
          title:
            points.length > 1 ? `Submit these ${points.length} requests?` : 'Submit this request?',
          message: 'It goes to a reviewer, who decides what happens next.',
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

    // One point posts a plain request, several post a batch. Nobody filling the form learns those
    // are different things — but a batch wrapper around a lone request would put a "submission"
    // around work that never came with anything else.
    if (points.length === 1) {
      this.sendOne(points[0]);
      return;
    }

    this.api
      .createBatch({
        clientName: this.clientName.trim() || null,
        items: points.map((p) => ({
          title: titleOf(p.text),
          description: describe(p),
          // Per item: urgency and a date are about one thing, not about the submission.
          requestedUrgency: p.urgency,
          targetDate: p.neededBy ? p.neededBy.toISOString() : null,
        })),
      })
      .subscribe({
        next: (batch) => {
          const uploads = batch.items.map((item, i) => ({
            id: item.id,
            files: points[i]?.files ?? [],
          }));
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
        requestedUrgency: point.urgency,
        clientName: this.clientName.trim() || null,
        targetDate: point.neededBy ? point.neededBy.toISOString() : null,
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
   * Files go up after the records exist, because an attachment needs something to hang off. A
   * failed upload does not fail the submission — the request is already saved, and saying otherwise
   * would be a lie.
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
            : `${failed} attachments did not upload. You can add them on the request.`,
        );
      }
      done();
    };

    for (const job of jobs) {
      this.api.uploadRequestAttachment(job.id, job.file).subscribe({
        next: finish,
        error: () => {
          failed++;
          finish();
        },
      });
    }
  }
}

function blankPoint(): Point {
  return {
    text: '',
    description: '',
    urgency: 'Normal',
    neededBy: null,
    files: [],
    expectedResult: '',
    currentResult: '',
    businessImpact: '',
    reproductionSteps: '',
    shown: {},
    open: { description: false, files: false },
  };
}

/** The first line, which is what somebody skimming a queue reads. */
function titleOf(text: string): string {
  const first = text.trim().split('\n')[0].trim();
  return first.length <= 300 ? first : `${first.slice(0, 299).trimEnd()}…`;
}

/** The point in full, with whatever optional detail was filled in folded underneath it. */
function describe(point: Point): string {
  const parts = [point.text.trim()];

  if (point.description.trim()) parts.push(point.description.trim());

  for (const field of DETAIL_FIELDS) {
    const value = (point as unknown as Record<string, string>)[field.key]?.trim();
    if (value) parts.push(`${field.label}: ${value}`);
  }

  return parts.join('\n\n');
}
