import { Component, input, output } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

/** Which column a list is ordered by, and which way. */
export interface SortState {
  by: string | null;
  descending: boolean;
}

/**
 * A clickable column heading.
 *
 * Clicking cycles newest/largest first → oldest/smallest first → off, so there is always a way back
 * to the list's natural order without hunting for a reset. The arrow only appears on the column
 * actually in use; a row of arrows on every heading reads as decoration rather than state.
 *
 * Sorting is applied by the server, not to the rows already on screen — ordering one page of
 * twenty-five would reorder the page rather than the list, which looks correct right up until the
 * data spans more than one page.
 */
@Component({
  selector: 'app-sort-header',
  standalone: true,
  imports: [MatIconModule],
  template: `
    <button type="button" class="head" (click)="cycle()" [attr.aria-label]="'Sort by ' + label()">
      <span>{{ label() }}</span>
      @if (active()) {
        <mat-icon>{{ sort().descending ? 'arrow_downward' : 'arrow_upward' }}</mat-icon>
      }
    </button>
  `,
  styles: `
    .head {
      display: inline-flex; align-items: center; gap: 3px;
      background: none; border: 0; padding: 0; margin: 0;
      font: inherit; color: inherit; cursor: pointer; white-space: nowrap;
    }
    .head:hover { color: var(--text); text-decoration: underline; }
    mat-icon { font-size: 15px; width: 15px; height: 15px; }
  `,
})
export class SortHeaderComponent {
  readonly label = input.required<string>();
  /** The value sent to the API for this column. */
  readonly column = input.required<string>();
  readonly sort = input.required<SortState>();

  readonly sortChange = output<SortState>();

  active = () => this.sort().by === this.column();

  cycle(): void {
    if (!this.active()) {
      this.sortChange.emit({ by: this.column(), descending: true });
      return;
    }

    // Third click clears it rather than looping, so the natural order is always one click away.
    this.sortChange.emit(
      this.sort().descending
        ? { by: this.column(), descending: false }
        : { by: null, descending: true });
  }
}
