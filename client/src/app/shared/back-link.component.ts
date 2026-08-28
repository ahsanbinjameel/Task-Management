import { Component, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { NavigationHistory } from '../core/navigation-history';

/**
 * The way back, without reaching for the sidebar.
 *
 * Every detail screen has a parent list, and until now the only way to it was the left-hand
 * menu — which is the wrong instrument twice over: it does not know you came from the assignment
 * queue rather than from Tasks, and using it means aiming at a different part of the screen to undo
 * a click you made a second ago.
 *
 * So this goes back to where the reader actually came from when that is known, and to the named
 * parent when it is not. The fallback is what makes it safe on a page opened from a notification or
 * a pasted link, where there is no in-app history to walk.
 */
@Component({
  selector: 'app-back-link',
  standalone: true,
  imports: [MatButtonModule, MatIconModule],
  template: `
    <button matButton class="back" type="button" (click)="back()">
      <mat-icon>arrow_back</mat-icon> {{ label() }}
    </button>
  `,
  styles: `
    .back { margin-left: -8px; color: var(--text-muted); }
    .back:hover { color: var(--text); }
  `,
})
export class BackLinkComponent {
  private readonly router = inject(Router);
  private readonly history = inject(NavigationHistory);

  /** Where to go when there is no in-app history — the screen this one belongs under. */
  readonly fallback = input.required<string>();

  /** What the parent is called. Shown as-is, so pass the word the nav uses. */
  readonly label = input('Back');

  back(): void {
    void this.router.navigateByUrl(this.history.previous() ?? this.fallback());
  }
}
