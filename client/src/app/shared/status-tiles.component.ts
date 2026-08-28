import { Component, input, output } from '@angular/core';
import { StatusCountDto } from '../core/models';

/**
 * The row of clickable counts above a list — the primary navigation, not a summary.
 *
 * One tile is always "All", and the selected one is the filter — clicking it again clears it, so
 * there is no separate "reset" control to find. Counts come from the server under the same filters
 * as the list, minus the view itself; counting locally would only ever describe the page you can
 * already see.
 *
 * Every tile is always present, even at zero, so its position can be learned. A tile that moves
 * about as work flows through the system is a tile nobody aims at.
 */
@Component({
  selector: 'app-status-tiles',
  standalone: true,
  template: `
    <div class="tiles" role="tablist">
      <button
        type="button"
        class="tile"
        [class.on]="selected() === null"
        (click)="pick.emit(null)">
        <span class="n">{{ total() }}</span>
        <span class="l">All</span>
      </button>

      @for (c of counts(); track c.key) {
        <button
          type="button"
          class="tile"
          [class.on]="selected() === c.key"
          [class.empty]="c.count === 0"
          (click)="pick.emit(selected() === c.key ? null : c.key)">
          <span class="n">{{ c.count }}</span>
          <span class="l">{{ c.label }}</span>
        </button>
      }
    </div>
  `,
  styles: `
    .tiles {
      /*
       * Wrapped, never scrolled sideways. A hidden tile is a count nobody knows to look for, and
       * the whole job of this strip is to say how much there is of each kind — a horizontal
       * scrollbar makes that answer conditional on noticing the scrollbar.
       */
      display: flex; flex-wrap: wrap; gap: 8px; padding: 2px 2px 8px;
      scrollbar-width: thin;
    }
    .tile {
      flex: 0 0 auto; min-width: 104px; text-align: left; cursor: pointer;
      display: flex; flex-direction: column; gap: 2px;
      padding: 9px 13px; border-radius: 10px;
      border: 1px solid var(--border); background: var(--surface);
      font: inherit; color: inherit;
      transition: border-color .12s, background .12s;
    }
    .tile:hover { border-color: var(--border-strong); }
    .tile.on { border-color: #1d69d4; background: var(--tone-running-bg); }
    .tile.empty .n { color: var(--text-muted); }
    .n { font-size: 19px; font-weight: 600; line-height: 1.1; }
    .l { font-size: 12px; color: var(--text-muted); line-height: 1.25; }

    @media (max-width: 700px) {
      .tile { min-width: 92px; padding: 8px 11px; }
      .n { font-size: 17px; }
    }
  `,
})
export class StatusTilesComponent {
  readonly counts = input.required<StatusCountDto[]>();
  /** The view currently filtering the list, or null for all. */
  readonly selected = input<string | null>(null);
  readonly total = input.required<number>();

  readonly pick = output<string | null>();
}
