import { Component, inject } from '@angular/core';
import { HttpContext } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ApiService } from '../../core/api.service';
import { FormSubmit } from '../../core/form-submit';
import { RequestDetailDto } from '../../core/models';

export interface FollowUpDialogData {
  /** The request this one comes out of. */
  requestId: number;
  requestNumber: string;
  clientName?: string | null;
  productLocation?: string | null;
  /** The round the original is on. The new one lands on the next. */
  round: number;
}

/**
 * Raising a point found in a later round of testing (PRODUCT-CORE §6).
 *
 * This is the answer to the case the plan calls the Faisal rule: detail-report points on day one,
 * master-report points on day two, and a timeline that quietly absorbs the second lot. The software
 * answer is not to punish the requester for testing properly, and not to let the new points
 * disappear into work already committed. It is to make the later round cheap to raise and visible
 * as a later round.
 *
 * So this asks for one thing — what is wrong — and says plainly what will happen: a new request
 * with its own number, carrying the shared context, leaving the running work's finish line alone.
 * The dialog is its own confirmation and does not open a second one: it names what is being acted
 * on, states the consequence and labels its own button.
 */
@Component({
  selector: 'app-follow-up-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatIconModule,
  ],
  template: `
    <h2 mat-dialog-title>Something else, found later</h2>

    <mat-dialog-content>
      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <mat-form-field class="full">
        <mat-label>What is wrong, or what you need</mat-label>
        <textarea matInput rows="3" name="title" [(ngModel)]="text" cdkFocusInitial
                  placeholder="Master report still shows the old total"></textarea>
      </mat-form-field>

      <div class="carried">
        <p class="muted small">This is carried over, so you do not retype it:</p>
        <ul class="muted small">
          <li>{{ data.clientName || 'No client — internal work' }}</li>
          @if (data.productLocation) { <li>{{ data.productLocation }}</li> }
          <li>Linked to {{ data.requestNumber }} · round {{ data.round + 1 }}</li>
        </ul>
      </div>

      <p class="note small">
        <mat-icon>schedule</mat-icon>
        <span>
          This gets its own number and is looked at on its own. It does
          <strong>not</strong> change the deadline of the work already in hand —
          that is the point of raising it separately.
        </span>
      </p>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [disabled]="!text.trim() || form.busy()" (click)="save()">
        {{ form.busy() ? 'Raising…' : 'Raise it separately' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .full { width: 100%; }
    mat-dialog-content { min-width: min(460px, 84vw); padding-top: 8px !important; }
    .carried { margin: -4px 0 12px; }
    .carried ul { margin: 4px 0 0; padding-left: 18px; }
    .carried li { margin-bottom: 2px; }
    .note {
      display: flex; gap: 8px; align-items: flex-start; margin: 0;
      padding: 10px 12px; border-radius: 8px; line-height: 1.45;
      background: var(--tone-warn-bg); color: var(--tone-warn-fg);
    }
    .note mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 12px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
  `,
})
export class FollowUpDialog {
  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<FollowUpDialog, RequestDetailDto>);
  readonly data = inject<FollowUpDialogData>(MAT_DIALOG_DATA);
  readonly form = new FormSubmit();

  text = '';

  save(): void {
    this.ref.disableClose = true;
    this.form.run(
      (ctx: HttpContext) => this.api.followUpRequest(
        this.data.requestId,
        { title: firstLine(this.text), description: this.text.trim() },
        ctx),
      (created) => { this.ref.disableClose = false; this.ref.close(created); },
    );
  }
}

/** The first line is what a queue shows; the whole text is the request. */
function firstLine(text: string): string {
  const first = text.trim().split('\n')[0].trim();
  return first.length <= 300 ? first : `${first.slice(0, 299).trimEnd()}…`;
}
