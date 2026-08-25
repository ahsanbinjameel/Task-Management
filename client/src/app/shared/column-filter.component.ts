import {
  Component, DestroyRef, HostBinding, computed, inject, input, output, signal,
} from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { OverlayModule } from '@angular/cdk/overlay';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { Subject, debounceTime } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

/**
 * What one column can be filtered by.
 *
 * `key` is sent to the server as `col[key]`, and the service that owns the table decides what it
 * means. A column with no spec renders an empty cell — the filter row is generated from the grid's
 * own column list, so a column nobody wrote a filter for simply has none rather than breaking the
 * alignment.
 */
export interface ColumnFilterSpec {
  /** Matches the column name in the grid's `displayedColumns`. */
  key: string;
  kind: 'text' | 'select' | 'date';
  /** Placeholder for text, or the "any" label for a select. */
  placeholder?: string;
  /** For `select` only. */
  options?: { value: string | number; label: string }[];
  /**
   * Set on a select whose values could contain the separator. Such a column can only carry one
   * value at a time. Nothing needs it today: every select filters by an enum name or a numeric id,
   * neither of which can contain a pipe.
   */
  singleOnly?: boolean;
  /**
   * Minimum width for this column's filter cell, in pixels.
   *
   * The filter row sets the floor for the whole column, so this is how a title column is kept wide
   * enough to read: at the default a long title wrapped onto four lines while its filter box sat in
   * a needlessly narrow cell.
   */
  minWidth?: number;
  /**
   * The values still reachable given the *other* columns' filters, from the server's
   * filter-options call. Undefined means "not known yet" and everything is offered — a grid that
   * has not answered yet must not look empty.
   *
   * Options outside this set are hidden rather than disabled: a list of choices that lead nowhere
   * is the thing this feature exists to remove.
   */
  available?: ReadonlySet<string>;
}

/** The current filter values, as the API wants them: `col[key] = value`. */
export type ColumnFilterValues = Record<string, string>;

/**
 * How several values for one column travel. Mirrors `ColumnFilters.Many` server-side, and must stay
 * a pipe: a comma does not survive ASP.NET's query binding — see the note there.
 */
const SEPARATOR = '|';

/**
 * One cell of the filter row.
 *
 * **Why this is not `app-search-select`.** That control is the app's only dropdown everywhere else,
 * deliberately — but it is a `mat-form-field`, which brings a floating label, an outline and a
 * subscript line: roughly 56px, against a table header row of about 30. Using it here would make
 * the filter row taller than three data rows. So this borrows the part that mattered — type to
 * narrow, works the same at four options and four hundred — and drops the chrome.
 *
 * A select holds **several values**: "Critical and High" is the question people actually ask of a
 * priority column, and allowing only one turns it into two page loads. The trigger stays one line
 * whatever is chosen (`Critical +2`), because a header cell that grows with the selection pushes
 * the whole grid down.
 */
@Component({
  selector: 'app-column-filter',
  standalone: true,
  imports: [FormsModule, OverlayModule, MatIconModule, MatButtonModule],
  template: `
    @if (spec(); as s) {
      <div class="cell" [style.min-width.px]="s.minWidth ?? null">
        @switch (s.kind) {
          @case ('select') {
            <button type="button" class="control trigger" [class.on]="selected().length > 0"
                    cdkOverlayOrigin #origin="cdkOverlayOrigin"
                    (click)="toggleOpen()"
                    [attr.aria-label]="ariaLabel(s)" [attr.aria-expanded]="open()">
              <span class="text" [class.placeholder]="selected().length === 0">
                {{ triggerLabel(s) }}
              </span>
              @if (selected().length > 1) {
                <span class="count">+{{ selected().length - 1 }}</span>
              }
              <mat-icon class="caret">arrow_drop_down</mat-icon>
            </button>

            <ng-template cdkConnectedOverlay
                         [cdkConnectedOverlayOrigin]="origin"
                         [cdkConnectedOverlayOpen]="open()"
                         [cdkConnectedOverlayMinWidth]="220"
                         (overlayOutsideClick)="close()"
                         (detach)="close()">
              <div class="panel" (keydown.escape)="close()">
                @if ((s.options ?? []).length > 7) {
                  <div class="search">
                    <mat-icon>search</mat-icon>
                    <input type="text" [(ngModel)]="term" placeholder="Type to narrow"
                           aria-label="Narrow the options" />
                  </div>
                }

                <div class="options" role="listbox">
                  @for (o of shown(s); track o.value) {
                    <label class="option" [class.checked]="isChecked(o.value)">
                      <input type="checkbox" [checked]="isChecked(o.value)"
                             (change)="choose(s, o.value)" />
                      <span>{{ o.label }}</span>
                    </label>
                  } @empty {
                    <p class="none">Nothing matches that.</p>
                  }
                </div>

                <div class="foot">
                  <button type="button" class="link" (click)="emit('')"
                          [disabled]="selected().length === 0">Clear</button>
                  <span class="grow"></span>
                  <button type="button" class="link strong" (click)="close()">Done</button>
                </div>
              </div>
            </ng-template>
          }

          @case ('date') {
            <input class="control" type="date" [ngModel]="value()" (ngModelChange)="emit($event)"
                   [attr.aria-label]="ariaLabel(s)" />
          }

          @default {
            <span class="text-wrap">
              <input class="control" type="text" [ngModel]="value()" (ngModelChange)="emit($event)"
                     [placeholder]="s.placeholder ?? 'Filter'" [attr.aria-label]="ariaLabel(s)" />
              @if (value()) {
                <button type="button" class="clear" (click)="emit('')"
                        aria-label="Clear this filter">
                  <mat-icon>close</mat-icon>
                </button>
              }
            </span>
          }
        }
      </div>
    }
  `,
  styles: `
    /*
     * A real minimum width, not zero.
     *
     * The first version let the inputs collapse, and because the table is 100% wide the browser
     * then squeezed the columns to fit: the task-number column wrapped "TSK-000003" onto two lines
     * and the priority filter rendered as "An". Columns that cannot fit should make the table
     * scroll — the table-scroll wrapper already handles that — rather than crush their neighbours.
     */
    .cell { position: relative; display: block; min-width: 112px; }
    :host([data-kind='date']) .cell { min-width: 140px; }
    .text-wrap { position: relative; display: block; }

    .control {
      width: 100%; box-sizing: border-box;
      height: 28px; padding: 0 8px;
      font: inherit; font-size: 12.5px; font-weight: 400; line-height: 26px;
      color: var(--text); background: var(--surface);
      border: 1px solid var(--border); border-radius: 6px;
      text-transform: none; letter-spacing: normal;
    }
    .control:hover { border-color: var(--border-strong); }
    .control:focus { outline: none; border-color: #1d69d4; box-shadow: 0 0 0 2px rgb(29 105 212 / 0.16); }
    input.control::placeholder { color: var(--text-muted); font-weight: 400; }

    /* --- the multi-select trigger --- */
    .trigger {
      display: flex; align-items: center; gap: 5px;
      text-align: left; cursor: pointer; padding-right: 3px;
    }
    .trigger.on {
      border-color: #1d69d4; background: var(--tone-running-bg); color: var(--tone-running-fg);
      font-weight: 500;
    }
    .trigger .text { flex: 1 1 auto; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .trigger .text.placeholder { color: var(--text-muted); }
    .trigger .count {
      flex: none; font-size: 11px; font-weight: 600;
      padding: 0 5px; border-radius: 999px;
      background: rgb(29 105 212 / 0.18);
    }
    .caret { flex: none; font-size: 18px; width: 18px; height: 18px; color: var(--text-muted); }
    .trigger.on .caret { color: inherit; }

    /* --- the clear button on a text filter --- */
    .clear {
      position: absolute; right: 3px; top: 50%; transform: translateY(-50%);
      display: grid; place-items: center;
      width: 18px; height: 18px; padding: 0;
      border: 0; background: none; cursor: pointer; color: var(--text-muted);
    }
    .clear:hover { color: var(--text); }
    .clear mat-icon { font-size: 14px; width: 14px; height: 14px; }

    /* --- the dropdown --- */
    .panel {
      margin-top: 4px; min-width: 220px; max-width: 320px;
      background: var(--surface-raised); border: 1px solid var(--border);
      border-radius: 10px; box-shadow: 0 8px 24px rgb(16 24 40 / 0.14);
      overflow: hidden; font-size: 13px;
      text-transform: none; letter-spacing: normal; font-weight: 400; color: var(--text);
    }
    .search {
      display: flex; align-items: center; gap: 6px;
      padding: 7px 10px; border-bottom: 1px solid var(--border);
    }
    .search mat-icon { font-size: 16px; width: 16px; height: 16px; color: var(--text-muted); }
    .search input {
      flex: 1 1 auto; border: 0; outline: none; font: inherit; background: none; color: inherit;
    }

    .options { max-height: 264px; overflow-y: auto; padding: 4px; }
    .option {
      display: flex; align-items: center; gap: 8px;
      padding: 6px 8px; border-radius: 6px; cursor: pointer;
    }
    .option:hover { background: var(--surface-sunken); }
    .option.checked { color: var(--tone-running-fg); font-weight: 500; }
    .option input { margin: 0; accent-color: #1d69d4; cursor: pointer; }
    .option span { flex: 1 1 auto; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .none { margin: 0; padding: 12px 10px; color: var(--text-muted); }

    .foot {
      display: flex; align-items: center; gap: 8px;
      padding: 6px 8px; border-top: 1px solid var(--border); background: var(--surface-sunken);
    }
    .grow { flex: 1 1 auto; }
    .link {
      border: 0; background: none; font: inherit; font-size: 12.5px;
      color: var(--text-muted); cursor: pointer; padding: 3px 6px; border-radius: 5px;
    }
    .link:hover:not(:disabled) { background: var(--surface); color: var(--text); }
    .link:disabled { opacity: .45; cursor: default; }
    .link.strong { color: #1d69d4; font-weight: 600; }
  `,
})
export class ColumnFilterComponent {
  /** Lets the stylesheet give a date input the extra room its picker needs. */
  @HostBinding('attr.data-kind')
  get kind(): string | null { return this.spec()?.kind ?? null; }

  readonly spec = input<ColumnFilterSpec | undefined>();
  readonly value = input<string>('');
  readonly changed = output<string>();

  readonly open = signal(false);
  term = '';

  /** The chosen values, split back out of what is on the wire. */
  readonly selected = computed(() =>
    this.value() ? this.value().split(SEPARATOR).filter((v) => v !== '') : []);

  ariaLabel = (s: ColumnFilterSpec) => `Filter by ${s.placeholder ?? s.key}`;

  /**
   * One line, always. Names the first choice and counts the rest beside it — listing them all would
   * either wrap the header row or truncate to "Critic…", which says less than "Critical +2".
   */
  triggerLabel(s: ColumnFilterSpec): string {
    const chosen = this.selected();
    if (chosen.length === 0) return s.placeholder ?? 'Any';

    const first = (s.options ?? []).find((o) => String(o.value) === chosen[0]);
    return first?.label ?? chosen[0];
  }

  /**
   * What the panel lists: reachable values, plus anything already ticked.
   *
   * Keeping the ticked ones visible even when they have become unreachable matters — narrowing by
   * another column can strand a value you chose earlier, and hiding it would leave the grid
   * filtered by something with no way to untick it. Exactly the dead end the empty-grid fix
   * removed, in miniature.
   */
  shown(s: ColumnFilterSpec): { value: string | number; label: string }[] {
    const options = s.options ?? [];
    const available = s.available;
    const chosen = this.selected();

    const reachable = available
      ? options.filter((o) => available.has(String(o.value)) || chosen.includes(String(o.value)))
      : options;

    const term = this.term.trim().toLowerCase();
    return term ? reachable.filter((o) => o.label.toLowerCase().includes(term)) : reachable;
  }

  isChecked = (value: string | number): boolean => this.selected().includes(String(value));

  toggleOpen(): void {
    this.term = '';
    this.open.set(!this.open());
  }

  close(): void {
    this.open.set(false);
  }

  /**
   * The panel stays open on a choice. Picking two statuses is one action to the person doing it, and
   * a menu that closed after the first would make the second a fresh trip.
   */
  choose(s: ColumnFilterSpec, value: string | number): void {
    const token = String(value);
    const chosen = this.selected();

    if (s.singleOnly) {
      this.emit(chosen.includes(token) ? '' : token);
      this.close();
      return;
    }

    const next = chosen.includes(token)
      ? chosen.filter((v) => v !== token)
      : [...chosen, token];

    this.emit(next.join(SEPARATOR));
  }

  emit(value: string): void {
    this.changed.emit(value ?? '');
  }
}

/**
 * "Nothing matched" — shown *underneath* a table that is still on screen.
 *
 * This exists because of a dead end. Every grid rendered its empty state *instead of* the table, so
 * a filter that matched nothing took the filter row away with it: the control that caused the
 * problem vanished, and the only way out was a page reload. Worse, the message was the unfiltered
 * one — the people grid announced "No accounts yet" while several accounts existed and one filter
 * was set.
 *
 * So when a filter is active the table stays (header, filter row, no body rows) and this sits below
 * it, saying what actually happened and offering the way out.
 */
@Component({
  selector: 'app-no-matches',
  standalone: true,
  imports: [MatButtonModule, MatIconModule],
  template: `
    <div class="none">
      <mat-icon>filter_alt_off</mat-icon>
      <p>{{ message() }}</p>
      <button matButton (click)="clear.emit()">Clear filters</button>
    </div>
  `,
  styles: `
    .none {
      display: flex; flex-direction: column; align-items: center; gap: 8px;
      padding: 30px 16px 34px; text-align: center;
    }
    .none mat-icon { color: var(--text-muted); font-size: 26px; width: 26px; height: 26px; }
    .none p { margin: 0; color: var(--text-muted); font-size: 13.5px; }
  `,
})
export class NoMatchesComponent {
  readonly message = input('Nothing matches those filters.');
  readonly clear = output<void>();
}

/**
 * One button that says how much is filtered and undoes all of it.
 *
 * Was a full-width banner, which spent the whole width of the screen on a sentence and read as a
 * warning rather than a control. It is a control, so it looks like one and takes the room a button
 * takes. Still above the grid rather than in the filter row, because on a wide table that row can
 * be scrolled out of view sideways and the state has to be visible from where the results are.
 */
@Component({
  selector: 'app-filter-summary',
  standalone: true,
  imports: [MatIconModule],
  template: `
    @if (count() > 0) {
      <div class="wrap">
        <button type="button" class="pill" (click)="clear.emit()">
          <mat-icon>filter_alt_off</mat-icon>
          <span>Clear {{ count() }} {{ count() === 1 ? 'filter' : 'filters' }}</span>
        </button>
      </div>
    }
  `,
  styles: `
    .wrap { display: flex; margin-bottom: 10px; }
    .pill {
      display: inline-flex; align-items: center; gap: 6px;
      padding: 5px 12px 5px 9px; border-radius: 999px;
      border: 1px solid #1d69d4;
      background: var(--tone-running-bg); color: var(--tone-running-fg);
      font: inherit; font-size: 12.5px; font-weight: 500; cursor: pointer;
    }
    .pill:hover { background: rgb(29 105 212 / 0.18); }
    .pill mat-icon { font-size: 16px; width: 16px; height: 16px; }
  `,
})
export class FilterSummaryComponent {
  readonly count = input.required<number>();
  readonly clear = output<void>();
}

/**
 * The filter row's state, shared by every grid that has one.
 *
 * Typing is debounced and a choice is not: waiting 300ms after ticking a box is latency for
 * nothing, whereas firing a request per keystroke is a request per keystroke.
 *
 * `asObject()` is what goes on the wire. Empty values are dropped rather than sent as `col[x]=`, so
 * a cleared filter looks to the server exactly like one that was never set.
 */
export class ColumnFilterState {
  private readonly values = signal<ColumnFilterValues>({});
  private readonly typed = new Subject<void>();
  private readonly immediate = new Subject<void>();

  readonly current = this.values.asReadonly();

  constructor(
    private readonly onChange: () => void,
    destroyRef: DestroyRef,
    debounceMs = 300,
  ) {
    this.typed.pipe(debounceTime(debounceMs), takeUntilDestroyed(destroyRef))
      .subscribe(() => this.onChange());

    this.immediate.pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => this.onChange());
  }

  value = (key: string): string => this.values()[key] ?? '';

  /** True when anything is narrowing the grid — drives the "clear all" affordance. */
  readonly any = computed(() => Object.values(this.values()).some((v) => v !== ''));

  /** How many columns are narrowing it, for the summary above the grid. */
  readonly activeCount = computed(() =>
    Object.values(this.values()).filter((v) => v !== '').length);

  set(spec: ColumnFilterSpec | undefined, key: string, value: string): void {
    this.values.update((all) => {
      const next = { ...all };
      if (value) next[key] = value; else delete next[key];
      return next;
    });

    if (spec?.kind === 'text') this.typed.next();
    else this.immediate.next();
  }

  clear(): void {
    if (!this.any()) return;
    this.values.set({});
    this.immediate.next();
  }

  /** `col[key]=value` for each set filter. Nothing when the row is empty. */
  params(base: HttpParams = new HttpParams()): HttpParams {
    let params = base;
    for (const [key, value] of Object.entries(this.values())) {
      if (value) params = params.set(`col[${key}]`, value);
    }
    return params;
  }

  /** The same thing as a plain object, for callers that build their own query bag. */
  asObject(): Record<string, string> {
    const out: Record<string, string> = {};
    for (const [key, value] of Object.entries(this.values())) {
      if (value) out[`col[${key}]`] = value;
    }
    return out;
  }
}

/** Convenience for a component: `readonly filters = columnFilters(() => this.reload());` */
export function columnFilters(onChange: () => void): ColumnFilterState {
  return new ColumnFilterState(onChange, inject(DestroyRef));
}
