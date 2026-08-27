import { Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ApiService } from '../core/api.service';
import { AttachmentDto, AttachmentKind } from '../core/models';

/**
 * Files added to a record that already exists — proof on a task, evidence on a check.
 *
 * The sibling `app-file-drop` stages files for a form that has not been submitted yet, because
 * there is nothing to attach them to until it is. Here the task is already there, so a file goes
 * straight up and comes back as an attachment: nothing to remember to press afterwards, and no
 * half-filled form to lose.
 *
 * Same three ways in as the drop zone — choose, drag, paste — because a screenshot taken with
 * Win+Shift+S is on the clipboard and nowhere else, and that is exactly the file this control
 * exists for.
 */
@Component({
  selector: 'app-attachment-upload',
  standalone: true,
  imports: [MatButtonModule, MatIconModule],
  template: `
    <div class="zone" [class.over]="over()"
         (dragover)="onDragOver($event)" (dragleave)="over.set(false)" (drop)="onDrop($event)">
      <label>
        <input type="file" multiple hidden [disabled]="busy()" (change)="onChoose($event)" />
        <span matButton="outlined">
          <mat-icon>{{ icon() }}</mat-icon> {{ label() }}
        </span>
      </label>
      <span class="muted small">
        @if (busy()) {
          Uploading {{ pending() }} file{{ pending() === 1 ? '' : 's' }}…
        } @else {
          or drop them here — Ctrl + V pastes a screenshot
        }
      </span>
    </div>
  `,
  styles: `
    .zone {
      display: flex; align-items: center; flex-wrap: wrap; gap: 10px; margin-top: 10px;
      padding: 10px 12px; border: 1.5px dashed var(--border-strong); border-radius: 10px;
      background: var(--surface-sunken);
    }
    .zone.over { border-color: #1d69d4; background: var(--tone-running-bg); }
    label { cursor: pointer; }
  `,
})
export class AttachmentUploadComponent {
  private readonly api = inject(ApiService);

  /**
   * Exactly one owner, the same rule the server enforces on the way in. Two inputs rather than a
   * generic upload callback because the caller already knows which kind of record it is showing,
   * and an id is harder to get wrong than a function.
   */
  readonly taskId = input<number | null>(null);
  readonly verificationId = input<number | null>(null);

  /**
   * What the file is for. Ignored for a verification, where the only kind that makes sense is
   * evidence and the server fixes it anyway.
   */
  readonly kind = input<AttachmentKind>('General');
  readonly label = input('Add files');
  readonly icon = input('attach_file');

  /** One event per file, as it lands. The parent decides what the list it belongs to becomes. */
  readonly uploaded = output<AttachmentDto>();

  readonly over = signal(false);
  readonly pending = signal(0);
  readonly busy = () => this.pending() > 0;

  constructor() {
    // Document-level, like the drop zone's: people press Ctrl+V wherever they are looking. Only
    // one uploader is ever on screen at a time — proof is the assignee's and QC evidence is the
    // checker's, the same person can never be both on one task, and a verification screen shows
    // its uploader only to the one person it was assigned to.
    const onPaste = (event: ClipboardEvent) => {
      const pasted = Array.from(event.clipboardData?.items ?? [])
        .filter((i) => i.kind === 'file')
        .map((i) => i.getAsFile())
        .filter((f): f is File => f !== null);

      if (pasted.length === 0) return;
      event.preventDefault();
      this.upload(pasted.map((f) => (f.name && f.name !== 'image.png' ? f : renamed(f))));
    };

    document.addEventListener('paste', onPaste);
    inject(DestroyRef).onDestroy(() => document.removeEventListener('paste', onPaste));
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.over.set(true);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.over.set(false);
    this.upload(Array.from(event.dataTransfer?.files ?? []));
  }

  onChoose(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.upload(Array.from(input.files ?? []));
    // Cleared so choosing the same file twice still fires a change.
    input.value = '';
  }

  private upload(files: File[]): void {
    const verificationId = this.verificationId();
    const taskId = this.taskId();

    if (verificationId === null && taskId === null) return;

    for (const file of files) {
      this.pending.update((n) => n + 1);

      const request = verificationId !== null
        ? this.api.uploadVerificationAttachment(verificationId, file)
        : this.api.uploadTaskAttachment(taskId!, file, this.kind());

      request.subscribe({
        next: (attachment) => {
          this.pending.update((n) => n - 1);
          this.uploaded.emit(attachment);
        },
        // The refusal is already on screen as a toast, from the ProblemDetails interceptor.
        error: () => this.pending.update((n) => n - 1),
      });
    }
  }
}

/** A clipboard image, given a name that says when it arrived. */
function renamed(file: File): File {
  const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
  const extension = file.type.split('/')[1] ?? 'png';
  return new File([file], `screenshot-${stamp}.${extension}`, { type: file.type });
}
