import {
  Component, DestroyRef, computed, effect, inject, input, output, signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ApiService } from '../core/api.service';
import { AttachmentDto } from '../core/models';
import { saveBlob } from '../core/format';

/** Anything the browser will render as a picture. Everything else gets an icon and a name. */
export function isImage(contentType: string | null | undefined): boolean {
  return !!contentType && contentType.startsWith('image/');
}

export function readableSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/** What the viewer is showing: a loaded object URL plus the name to caption it with. */
interface ViewerItem {
  name: string;
  url: string;
}

/**
 * The full-size image viewer.
 *
 * Opening a screenshot should be looking at a screenshot. The alternative the app had — download,
 * find the file, open it in something else, come back — costs four actions and a context switch
 * to answer "is this the right screenshot?", which is the question people ask most.
 */
@Component({
  selector: 'app-image-viewer',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatTooltipModule],
  template: `
    <div class="viewer" (wheel)="onWheel($event)">
      <header>
        <span class="name">{{ current().name }}</span>
        @if (data.items.length > 1) {
          <span class="muted small">{{ index() + 1 }} of {{ data.items.length }}</span>
        }
        <span class="spacer"></span>
        <button matIconButton (click)="zoom(-0.25)" matTooltip="Zoom out"><mat-icon>remove</mat-icon></button>
        <button matIconButton (click)="reset()" matTooltip="Fit to screen"><mat-icon>fit_screen</mat-icon></button>
        <button matIconButton (click)="zoom(0.25)" matTooltip="Zoom in"><mat-icon>add</mat-icon></button>
        <button matIconButton (click)="download()" matTooltip="Download"><mat-icon>download</mat-icon></button>
        <button matIconButton mat-dialog-close matTooltip="Close"><mat-icon>close</mat-icon></button>
      </header>

      <div class="stage" (mousedown)="startPan($event)" (mousemove)="pan($event)"
           (mouseup)="endPan()" (mouseleave)="endPan()"
           [class.grabbing]="panning()">
        <img [src]="current().url" [alt]="current().name"
             [style.transform]="'translate(' + offset().x + 'px,' + offset().y + 'px) scale(' + scale() + ')'" />
      </div>

      @if (data.items.length > 1) {
        <button class="nav prev" matIconButton (click)="step(-1)" aria-label="Previous">
          <mat-icon>chevron_left</mat-icon>
        </button>
        <button class="nav next" matIconButton (click)="step(1)" aria-label="Next">
          <mat-icon>chevron_right</mat-icon>
        </button>
      }
    </div>
  `,
  styles: `
    .viewer { position: relative; width: min(92vw, 1200px); height: min(86vh, 900px);
              display: flex; flex-direction: column; background: #0e1116; color: #e8eaed; }
    header { display: flex; align-items: center; gap: 6px; padding: 8px 10px;
             border-bottom: 1px solid rgba(255,255,255,0.1); }
    .name { font-size: 13.5px; font-weight: 500; overflow: hidden;
            text-overflow: ellipsis; white-space: nowrap; max-width: 46%; }
    .muted { color: #9aa0a6; }
    .spacer { flex: 1; }
    header button { color: #e8eaed; }
    .stage { flex: 1; overflow: hidden; display: grid; place-items: center; cursor: grab; }
    .stage.grabbing { cursor: grabbing; }
    img { max-width: 100%; max-height: 100%; transform-origin: center;
          transition: transform .06s linear; user-select: none; -webkit-user-drag: none; }
    .nav { position: absolute; top: 50%; transform: translateY(-50%); color: #e8eaed;
           background: rgba(0,0,0,0.45); }
    .prev { left: 10px; }
    .next { right: 10px; }
  `,
})
export class ImageViewerDialog {
  readonly ref = inject(MatDialogRef<ImageViewerDialog>);
  readonly data = inject<{ items: ViewerItem[]; start: number }>(MAT_DIALOG_DATA);

  readonly index = signal(0);
  readonly scale = signal(1);
  readonly offset = signal({ x: 0, y: 0 });
  readonly panning = signal(false);
  private from = { x: 0, y: 0 };

  constructor() {
    this.index.set(Math.max(0, Math.min(this.data.start, this.data.items.length - 1)));

    // Arrow keys move between images; Escape is Material's own.
    this.ref.keydownEvents().subscribe((event) => {
      if (event.key === 'ArrowLeft') this.step(-1);
      if (event.key === 'ArrowRight') this.step(1);
    });
  }

  readonly current = computed(() => this.data.items[this.index()]);

  step(by: number): void {
    const next = (this.index() + by + this.data.items.length) % this.data.items.length;
    this.index.set(next);
    this.reset();
  }

  zoom(by: number): void {
    this.scale.set(Math.min(6, Math.max(0.25, this.scale() + by)));
  }

  onWheel(event: WheelEvent): void {
    event.preventDefault();
    this.zoom(event.deltaY < 0 ? 0.2 : -0.2);
  }

  reset(): void {
    this.scale.set(1);
    this.offset.set({ x: 0, y: 0 });
  }

  startPan(event: MouseEvent): void {
    this.panning.set(true);
    this.from = { x: event.clientX - this.offset().x, y: event.clientY - this.offset().y };
  }

  pan(event: MouseEvent): void {
    if (!this.panning()) return;
    this.offset.set({ x: event.clientX - this.from.x, y: event.clientY - this.from.y });
  }

  endPan(): void {
    this.panning.set(false);
  }

  /** Still offered, just not the only way to look at a picture. */
  download(): void {
    const link = document.createElement('a');
    link.href = this.current().url;
    link.download = this.current().name;
    link.click();
  }
}

/**
 * A list of attachments, with pictures shown as pictures.
 *
 * Images are fetched as blobs rather than pointed at with `<img src>`, because the endpoint wants
 * the bearer token and an `<img>` tag cannot carry one. The object URLs are released when the
 * component goes away.
 */
@Component({
  selector: 'app-attachments',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatTooltipModule],
  template: `
    @if (attachments().length === 0) {
      <p class="muted small" style="margin:0">{{ emptyText() }}</p>
    } @else {
      <div class="grid">
        @for (a of attachments(); track a.id) {
          @if (image(a)) {
            <figure class="shot" (click)="view(a)" [title]="a.fileName">
              @if (url(a.id); as src) {
                <img [src]="src" [alt]="a.fileName" />
              } @else {
                <div class="loading"><mat-icon>image</mat-icon></div>
              }
              <figcaption>{{ a.fileName }}</figcaption>
            </figure>
          } @else {
            <div class="file">
              <mat-icon>{{ icon(a.contentType) }}</mat-icon>
              <div class="meta">
                <span class="name">{{ a.fileName }}</span>
                <span class="muted small">{{ size(a.sizeBytes) }}</span>
              </div>
              <button matIconButton (click)="download(a)" matTooltip="Download">
                <mat-icon>download</mat-icon>
              </button>
              @if (removable()) {
                <button matIconButton (click)="remove.emit(a)" matTooltip="Remove">
                  <mat-icon>close</mat-icon>
                </button>
              }
            </div>
          }
        }
      </div>
    }
  `,
  styles: `
    .grid { display: flex; flex-wrap: wrap; gap: 10px; }
    .shot {
      margin: 0; width: 148px; cursor: pointer; border: 1px solid var(--border);
      border-radius: 10px; overflow: hidden; background: var(--surface-sunken);
    }
    .shot:hover { border-color: var(--border-strong); }
    .shot img, .shot .loading { display: block; width: 100%; height: 96px; object-fit: cover; }
    .shot .loading { display: grid; place-items: center; color: var(--text-muted); }
    figcaption {
      padding: 6px 8px; font-size: 12px; overflow: hidden;
      text-overflow: ellipsis; white-space: nowrap;
    }
    .file {
      display: flex; align-items: center; flex-wrap: wrap; gap: 10px; padding: 8px 10px;
      border: 1px solid var(--border); border-radius: 10px; min-width: 220px;
    }
    .file .meta { display: flex; flex-direction: column; min-width: 0; }
    .file .name { font-size: 13.5px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .file mat-icon { color: var(--text-muted); }
  `,
})
export class AttachmentsComponent {
  private readonly api = inject(ApiService);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);

  readonly attachments = input.required<AttachmentDto[]>();
  readonly emptyText = input('No files attached');
  readonly removable = input(false);
  readonly remove = output<AttachmentDto>();

  private readonly urls = signal<Record<number, string>>({});

  constructor() {
    // Load the pictures whenever the list changes; already-loaded ones are not fetched twice.
    effect(() => {
      for (const a of this.attachments()) {
        if (!isImage(a.contentType) || this.urls()[a.id]) continue;

        this.api.downloadAttachment(a.id).subscribe({
          next: (blob) => this.urls.update((all) => ({ ...all, [a.id]: URL.createObjectURL(blob) })),
          error: () => undefined,
        });
      }
    });

    this.destroyRef.onDestroy(() => {
      for (const url of Object.values(this.urls())) URL.revokeObjectURL(url);
    });
  }

  image = (a: AttachmentDto) => isImage(a.contentType);
  size = readableSize;
  url = (id: number) => this.urls()[id];

  icon(contentType: string): string {
    if (contentType.includes('pdf')) return 'picture_as_pdf';
    if (contentType.includes('zip') || contentType.includes('compressed')) return 'folder_zip';
    if (contentType.includes('sheet') || contentType.includes('excel')) return 'table_chart';
    if (contentType.includes('word') || contentType.includes('document')) return 'description';
    return 'insert_drive_file';
  }

  /** Opens the viewer on the clicked picture, with the other pictures alongside it. */
  view(clicked: AttachmentDto): void {
    const images = this.attachments().filter((a) => isImage(a.contentType) && this.urls()[a.id]);
    const items: ViewerItem[] = images.map((a) => ({ name: a.fileName, url: this.urls()[a.id] }));
    if (items.length === 0) return;

    this.dialog.open(ImageViewerDialog, {
      data: { items, start: images.findIndex((a) => a.id === clicked.id) },
      panelClass: 'viewer-panel',
      maxWidth: '96vw',
      autoFocus: false,
    });
  }

  download(a: AttachmentDto): void {
    this.api.downloadAttachment(a.id).subscribe((blob) => saveBlob(blob, a.fileName));
  }
}
