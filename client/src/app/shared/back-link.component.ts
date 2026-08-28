import { Component, inject, input } from '@angular/core';
import { Location } from '@angular/common';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { NavigationHistory } from '../core/navigation-history';

/**
 * Back and forward, without reaching for the sidebar.
 *
 * Two arrows and no words. An earlier version named the parent — "Back to Tasks" — which was wrong
 * about as often as it was right: the same task detail is reached from Tasks, from the assignment
 * queue and from the Quality page, and a control that names one of the three is telling two out of
 * three readers something untrue. The arrow says where it goes without claiming to know where that
 * is, and the tooltip carries the destination when there is one worth naming.
 *
 * Forward exists because back on its own is a trapdoor: a reader who steps back to check something
 * has no way to return to what they were reading except by finding it again.
 *
 * The fallback is what makes back safe. `history.back()` on a page opened from a notification, a
 * bookmark or a pasted link walks straight out of the application — which is exactly the case where
 * a back control is most wanted, so it cannot be the case where it misbehaves.
 */
@Component({
  selector: 'app-back-link',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatTooltipModule],
  template: `
    <nav class="nav-arrows" aria-label="Page history">
      <button matIconButton type="button" (click)="back()"
              [matTooltip]="backTooltip()" aria-label="Back">
        <mat-icon>arrow_back</mat-icon>
      </button>
      <button matIconButton type="button" (click)="forward()"
              matTooltip="Forward" aria-label="Forward">
        <mat-icon>arrow_forward</mat-icon>
      </button>
    </nav>
  `,
  styles: `
    .nav-arrows { display: flex; align-items: center; gap: 2px; margin: 0 0 4px -8px; }
    .nav-arrows button { color: var(--text-muted); }
    .nav-arrows button:hover { color: var(--text); }
  `,
})
export class BackLinkComponent {
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly history = inject(NavigationHistory);

  /** Where back goes when this page is the first thing the reader opened. */
  readonly fallback = input.required<string>();

  /**
   * Kept so existing call sites still compile and so the tooltip has something to say. It is no
   * longer rendered as a label — see the note on this class.
   */
  readonly label = input('Back');

  backTooltip(): string {
    return this.history.previous() ? 'Back' : `Back to ${this.label()}`;
  }

  /**
   * The browser's own back where there is somewhere in-app to go, so the reader's place in a long
   * grid, an open tab and any scroll position all come back with it. Only when there is no in-app
   * history does this fall through to routing at the parent.
   */
  back(): void {
    if (this.history.previous()) {
      this.location.back();
      return;
    }

    void this.router.navigateByUrl(this.fallback());
  }

  forward(): void {
    this.location.forward();
  }
}
