import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { ApiService } from '../../core/api.service';
import { FormSubmit } from '../../core/form-submit';
import { Priority, VerificationDetailDto, VerificationTargetType } from '../../core/models';
import { verificationTargetLabel } from '../../core/labels';
import { SearchSelectComponent, SelectOption, enumOptions } from '../../shared/search-select.component';

/**
 * Raising an independent check — one that belongs to no request and no task.
 *
 * This is its own confirmation: it names what is being checked, says plainly that nothing will be
 * created, and labels its own button. Stacking a `ConfirmDialog` on top would be a modal on a modal
 * for an action that is reversible anyway — a check can be called off.
 */
@Component({
  selector: 'app-verification-create-dialog',
  standalone: true,
  imports: [
    FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule,
    MatInputModule, SearchSelectComponent,
  ],
  template: `
    <h2 mat-dialog-title>Raise a check</h2>

    <mat-dialog-content>
      <p class="lead">
        Somebody is asked to find out whether a thing actually works. This creates no task — if
        there turns out to be real work behind it, that still goes through a request and an
        approval.
      </p>

      @if (form.message(); as m) {
        <div class="form-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ m }}</span>
        </div>
      }

      <div class="stack">
        <mat-form-field>
          <mat-label>What needs checking</mat-label>
          <input matInput name="title" [(ngModel)]="title" cdkFocusInitial maxlength="300"
                 (input)="form.clearField('title')" />
          @if (form.fieldError('title'); as e) { <mat-error>{{ e }}</mat-error> }
        </mat-form-field>

        <div class="row">
          <app-search-select label="Kind of thing" name="targetType"
                             [options]="targetOptions" [(ngModel)]="targetType" />
          <app-search-select label="Priority" name="priority"
                             [options]="priorityOptions" [(ngModel)]="priority" />
        </div>

        @if (targetType === 'Module') {
          <app-search-select label="Module" name="moduleId"
                             [options]="moduleOptions()" [(ngModel)]="moduleId" />
        } @else {
          <mat-form-field>
            <mat-label>{{ nameLabel() }}</mat-label>
            <input matInput name="targetName" [(ngModel)]="targetName" maxlength="300"
                   (input)="form.clearField('targetName')" />
            @if (form.fieldError('targetName'); as e) { <mat-error>{{ e }}</mat-error> }
          </mat-form-field>
        }

        <mat-form-field>
          <mat-label>Version, environment or link (optional)</mat-label>
          <input matInput name="targetReference" [(ngModel)]="targetReference" maxlength="300" />
        </mat-form-field>

        <mat-form-field>
          <mat-label>What should it do? (optional)</mat-label>
          <textarea matInput rows="2" name="expectedBehavior"
                    [(ngModel)]="expectedBehavior" maxlength="2000"></textarea>
        </mat-form-field>

        <mat-form-field>
          <mat-label>Instructions for the checker (optional)</mat-label>
          <textarea matInput rows="3" name="instructions"
                    [(ngModel)]="instructions" maxlength="4000"></textarea>
        </mat-form-field>

        <app-search-select label="Give it to" name="assignToUserId"
                           nullLabel="Leave for someone to pick up"
                           [options]="checkerOptions()" [(ngModel)]="assignToUserId" />
      </div>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close [disabled]="form.busy()">Cancel</button>
      <button matButton="filled" [disabled]="!title.trim() || form.busy()" (click)="save()">
        {{ form.busy() ? 'Raising…' : 'Raise the check' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content { padding-top: 8px !important; }
    .lead { margin: 0 0 14px; color: var(--text-muted); font-size: 13px; line-height: 1.5; }
    .form-error {
      display: flex; align-items: flex-start; gap: 8px; margin: 0 0 14px;
      padding: 10px 12px; border-radius: 8px; font-size: 13.5px; line-height: 1.45;
      background: var(--tone-danger-bg); color: var(--tone-danger-fg);
    }
    .form-error mat-icon { font-size: 18px; width: 18px; height: 18px; flex: none; margin-top: 1px; }
    /* A container that already spaces its children must null the field's own margin, or the two
       add up and the gap doubles. */
    .stack { display: flex; flex-direction: column; gap: 10px; }
    .stack mat-form-field, .stack app-search-select { margin: 0; width: 100%; }
    .row { display: flex; gap: 10px; }
    .row > * { flex: 1; min-width: 0; }
    @media (max-width: 560px) { .row { flex-direction: column; } }
  `,
})
export class VerificationCreateDialog implements OnInit {
  private readonly api = inject(ApiService);
  private readonly ref = inject(MatDialogRef<VerificationCreateDialog>);

  readonly form = new FormSubmit();

  title = '';
  instructions = '';
  expectedBehavior = '';
  targetType: VerificationTargetType = 'Form';
  targetName = '';
  targetReference = '';
  moduleId: number | null = null;
  priority: Priority = 'Normal';
  assignToUserId: number | null = null;

  readonly checkerOptions = signal<SelectOption[]>([]);
  readonly moduleOptions = signal<SelectOption[]>([]);

  // 'Request' is deliberately absent. That target is what triage produces when it routes a request;
  // offering it on an independent check would leave a target pointing at nothing.
  readonly targetOptions = enumOptions<VerificationTargetType>(
    ['Form', 'Module', 'Build', 'Other'], verificationTargetLabel);

  readonly priorityOptions = enumOptions<Priority>(['Critical', 'High', 'Normal', 'Low']);

  nameLabel = () => (this.targetType === 'Build' ? 'Which build' : 'Which form or screen');

  ngOnInit(): void {
    this.api.assignableCheckers().subscribe((checkers) => {
      this.checkerOptions.set(checkers.map((c) => ({
        value: c.userId,
        label: c.displayName,
        // The server already sorts lightest-first; saying how much they hold is what makes that
        // ordering readable rather than mysterious.
        hint: c.openVerifications === 0 ? 'free' : `${c.openVerifications} open`,
      })));
    });

    this.api.modules().subscribe((modules) => {
      this.moduleOptions.set(modules.map((m) => ({
        value: m.id,
        label: m.name,
        hint: m.projectName ?? undefined,
      })));
    });
  }

  save(): void {
    this.form.run(
      (context) => this.api.createVerification({
        title: this.title.trim(),
        instructions: this.instructions.trim() || null,
        expectedBehavior: this.expectedBehavior.trim() || null,
        targetType: this.targetType,
        moduleId: this.targetType === 'Module' ? this.moduleId : null,
        targetName: this.targetName.trim() || null,
        targetReference: this.targetReference.trim() || null,
        priority: this.priority,
        assignToUserId: this.assignToUserId,
      }, context),
      (created: VerificationDetailDto) => this.ref.close(created),
    );
  }
}
