import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { Observable } from 'rxjs';
import { saveBlob } from '../core/format';

export interface PdfViewerData {
  /** Shown in the toolbar — say what the document is, not what the button was called. */
  title: string;
  /** Used only if the reader chooses to keep a copy. */
  fileName: string;
  /** Fetched when the dialog opens, so the click is instant and the wait is visible. */
  load: () => Observable<Blob>;
}

/**
 * Looking at a PDF instead of collecting it.
 *
 * Every PDF in the app used to go straight to the downloads folder. That is the wrong default for
 * the question people actually ask — "what does today's report say?" — because answering it cost a
 * file on disk, a trip to the shell, an external viewer, and a folder that fills up with
 * `team-daily-2026-08-25.pdf` copies nobody deletes. Reading is the common case; keeping is the
 * rare one, so reading is what the click does and Download is a button in the toolbar.
 *
 * The bytes arrive over HTTP with the caller's bearer token, which an `<iframe src>` cannot carry,
 * so they are fetched here and handed to the frame as a blob URL — the same trick, and the same
 * reasoning, as the image viewer. That URL is same-origin, unguessable, and revoked when the dialog
 * closes; `frame-src blob:` in the CSP exists for exactly this and nothing else.
 *
 * Rendering is the browser's own PDF viewer, which brings paging, zoom, search and print for free.
 * Where there isn't one the frame comes up blank, so the toolbar always offers Download as the way
 * out rather than leaving the reader with an empty rectangle.
 */
@Component({
  selector: 'app-pdf-viewer',
  standalone: true,
  imports: [
    MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule,
  ],
  template: `
    <div class="bar">
      <mat-icon class="kind">picture_as_pdf</mat-icon>
      <span class="title" [title]="data.title">{{ data.title }}</span>
      <span class="spacer"></span>

      <button matIconButton (click)="download()" [disabled]="!blob()"
              matTooltip="Save a copy" aria-label="Download">
        <mat-icon>download</mat-icon>
      </button>
      <button matIconButton mat-dialog-close matTooltip="Close" aria-label="Close">
        <mat-icon>close</mat-icon>
      </button>
    </div>

    <div class="stage">
      @if (loading()) {
        <div class="state">
          <mat-spinner diameter="34" />
          <p class="muted small">Preparing the document…</p>
        </div>
      } @else if (failed()) {
        <div class="state">
          <mat-icon class="sad">error_outline</mat-icon>
          <p class="muted small">That document could not be loaded.</p>
        </div>
      } @else if (url(); as src) {
        <iframe [src]="src" title="PDF preview"></iframe>

        <!--
          Sits behind the frame. If the browser renders the PDF this is never seen; if it has no
          viewer the frame paints nothing and this shows through, so the reader is offered the file
          rather than a blank rectangle.
        -->
        <div class="fallback">
          <p class="muted small">This browser cannot show PDFs inline.</p>
          <button matButton="filled" (click)="download()">
            <mat-icon>download</mat-icon> Download it instead
          </button>
        </div>
      }
    </div>
  `,
  styles: `
    :host { display: block; }
    .bar {
      display: flex; align-items: center; gap: 8px;
      padding: 6px 8px 6px 14px; border-bottom: 1px solid var(--border);
    }
    .kind { color: var(--text-muted); flex: none; }
    .title {
      font-weight: 600; font-size: 14px;
      overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
    }
    .spacer { flex: 1 1 auto; }

    .stage { position: relative; height: min(78vh, 900px); background: var(--surface-2, #f3f4f6); }
    iframe { position: relative; z-index: 1; width: 100%; height: 100%; border: 0; display: block; }

    .fallback {
      position: absolute; inset: 0; z-index: 0;
      display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 10px;
    }

    .state {
      position: absolute; inset: 0;
      display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 10px;
    }
    .state p { margin: 0; }
    .sad { font-size: 30px; width: 30px; height: 30px; color: var(--text-muted); }
  `,
})
export class PdfViewerDialog implements OnInit, OnDestroy {
  readonly data = inject<PdfViewerData>(MAT_DIALOG_DATA);
  private readonly sanitizer = inject(DomSanitizer);

  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly blob = signal<Blob | null>(null);
  readonly url = signal<SafeResourceUrl | null>(null);

  private objectUrl: string | null = null;

  ngOnInit(): void {
    this.data.load().subscribe({
      next: (blob) => {
        this.blob.set(blob);
        this.objectUrl = URL.createObjectURL(blob);

        // #toolbar=0 is honoured by Chrome and Edge: the dialog has its own bar, and two stacked
        // toolbars leave the page itself barely visible. Browsers that ignore it are no worse off.
        this.url.set(
          this.sanitizer.bypassSecurityTrustResourceUrl(`${this.objectUrl}#toolbar=0&view=FitH`),
        );
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  ngOnDestroy(): void {
    // The blob would otherwise be held for the life of the tab — a report is a few hundred KB and
    // these are opened repeatedly.
    if (this.objectUrl) URL.revokeObjectURL(this.objectUrl);
  }

  download(): void {
    const blob = this.blob();
    if (blob) saveBlob(blob, this.data.fileName);
  }
}

/**
 * Opens the viewer at a consistent size.
 *
 * A helper rather than four call sites repeating the same dialog config, because a PDF that opens
 * letterbox-sized on one screen and full-bleed on another reads as two different features.
 */
export function openPdf(dialog: MatDialog, data: PdfViewerData): MatDialogRef<PdfViewerDialog> {
  return dialog.open<PdfViewerDialog, PdfViewerData>(PdfViewerDialog, {
    data,
    width: 'min(1000px, 96vw)',
    maxWidth: '96vw',
    panelClass: 'pdf-dialog',
    autoFocus: false,
  });
}
