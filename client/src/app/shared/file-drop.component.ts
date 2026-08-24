import { Component, DestroyRef, effect, inject, model, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { isImage, readableSize } from './attachments.component';

/**
 * The place files go before a record exists to hang them on.
 *
 * Three ways in, because people arrive with the file in three different states: already saved
 * (choose), in a folder they are looking at (drag), or on the clipboard from the snipping tool
 * (paste). The last is the common one for a bug report — Win+Shift+S then Ctrl+V — and it is the
 * one a plain file input cannot do at all.
 *
 * Pictures are shown as pictures straight away. The mistakes this catches — the wrong screenshot,
 * the same one twice, the file from the wrong folder — are ones you can only see by looking.
 */
@Component({
  selector: 'app-file-drop',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatTooltipModule],
  template: `
    <div class="zone" [class.over]="over()"
         (dragover)="onDragOver($event)" (dragleave)="over.set(false)" (drop)="onDrop($event)">
      <mat-icon>cloud_upload</mat-icon>
      <p class="lead">Drop screenshots or files here</p>
      <label>
        <input type="file" multiple hidden (change)="onChoose($event)" />
        <span matButton="outlined">Choose files</span>
      </label>
      <p class="muted small">You can also paste a screenshot with Ctrl + V</p>
    </div>

    @if (files().length) {
      <div class="picked">
        @for (f of files(); track f) {
          <figure class="item">
            @if (preview(f); as src) {
              <img [src]="src" [alt]="f.name" />
            } @else {
              <div class="doc"><mat-icon>description</mat-icon></div>
            }
            <figcaption>
              <span class="name" [title]="f.name">{{ f.name }}</span>
              <span class="muted small">{{ size(f.size) }}</span>
            </figcaption>
            <button type="button" class="remove" (click)="remove(f)"
                    [attr.aria-label]="'Remove ' + f.name">
              <mat-icon>close</mat-icon>
            </button>
          </figure>
        }
      </div>
    }
  `,
  styles: `
    .zone {
      display: flex; flex-direction: column; align-items: center; gap: 6px;
      padding: 20px; border: 1.5px dashed var(--border-strong); border-radius: 12px;
      background: var(--surface-sunken); text-align: center;
    }
    .zone.over { border-color: #1d69d4; background: var(--tone-running-bg); }
    .zone mat-icon { color: var(--text-muted); }
    .lead { margin: 0; font-size: 14px; font-weight: 500; }
    .zone p.muted { margin: 0; }
    label { cursor: pointer; }

    .picked { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 12px; }
    .item {
      position: relative; margin: 0; width: 140px;
      border: 1px solid var(--border); border-radius: 10px; overflow: hidden;
      background: var(--surface);
    }
    .item img, .item .doc { display: block; width: 100%; height: 88px; object-fit: cover; }
    .item .doc { display: grid; place-items: center; color: var(--text-muted); }
    figcaption { display: flex; flex-direction: column; padding: 6px 8px; min-width: 0; }
    .name { font-size: 12.5px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .remove {
      position: absolute; top: 4px; right: 4px; width: 24px; height: 24px;
      display: grid; place-items: center; border: none; border-radius: 50%;
      background: rgba(0,0,0,0.55); color: #fff; cursor: pointer; padding: 0;
    }
    .remove mat-icon { font-size: 16px; width: 16px; height: 16px; }
  `,
})
export class FileDropComponent {
  /** Two-way: the form owns the list, this owns how it is added to. */
  readonly files = model<File[]>([]);

  readonly over = signal(false);
  private readonly urls = signal<Map<File, string>>(new Map());

  constructor() {
    // Pasting is a document-level gesture: people press Ctrl+V wherever they happen to be looking,
    // not after clicking a particular box.
    const onPaste = (event: ClipboardEvent) => {
      const items = Array.from(event.clipboardData?.items ?? []);
      const pasted = items
        .filter((i) => i.kind === 'file')
        .map((i) => i.getAsFile())
        .filter((f): f is File => f !== null);

      if (pasted.length === 0) return;
      event.preventDefault();
      // Clipboard images arrive called "image.png" every time, which is useless once there are
      // two of them. Stamp them so the list can be read.
      this.add(pasted.map((f) => f.name && f.name !== 'image.png' ? f : renamed(f)));
    };

    document.addEventListener('paste', onPaste);

    // Thumbnails are made as files arrive and released as they leave, so the template only ever
    // reads. Runs on any change to the list, including one the form made.
    effect(() => {
      const current = this.files();

      for (const file of current) {
        if (isImage(file.type) && !this.urls().has(file)) {
          const url = URL.createObjectURL(file);
          this.urls.update((all) => new Map(all).set(file, url));
        }
      }

      for (const [file, url] of this.urls()) {
        if (!current.includes(file)) {
          URL.revokeObjectURL(url);
          this.urls.update((all) => {
            const next = new Map(all);
            next.delete(file);
            return next;
          });
        }
      }
    });

    const destroyRef = inject(DestroyRef);
    destroyRef.onDestroy(() => {
      document.removeEventListener('paste', onPaste);
      for (const url of this.urls().values()) URL.revokeObjectURL(url);
    });
  }

  size = readableSize;

  /**
   * Read-only: the URLs are made when the file arrives, not while the row is being drawn.
   * Creating one here would be a signal write during rendering, which Angular refuses — rightly,
   * since it makes what is on screen depend on the order things happened to be painted in.
   */
  preview(file: File): string | null {
    return this.urls().get(file) ?? null;
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.over.set(true);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.over.set(false);
    this.add(Array.from(event.dataTransfer?.files ?? []));
  }

  onChoose(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.add(Array.from(input.files ?? []));
    input.value = '';
  }

  private add(incoming: File[]): void {
    if (incoming.length === 0) return;
    this.files.update((current) => [...current, ...incoming]);
  }

  remove(file: File): void {
    this.files.update((current) => current.filter((f) => f !== file));
  }
}

/** A clipboard image, given a name that says when it arrived. */
function renamed(file: File): File {
  const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
  const extension = file.type.split('/')[1] ?? 'png';
  return new File([file], `screenshot-${stamp}.${extension}`, { type: file.type });
}
