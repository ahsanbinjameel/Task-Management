import { Component, HostBinding, computed, inject, input, output, signal } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { DestroyRef } from '@angular/core';
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
}

/** The current filter values, as the API wants them: `col[key] = value`. */
export type ColumnFilterValues = Record<string, string>;

/**
 * One cell of the filter row.
 *
 * Deliberately a plain `<input>`/`<select>` rather than the Material form field used everywhere
 * else: a filter row is one line under the header, and `mat-form-field` brings ~56px of label,
 * outline and hint space that would double the height of every table on the screen. This is the one
 * place in the app where that trade is worth making, and it is why `app-search-select` is not used
 * here either — the row has to stay the height of a table header.
 */
@Component({
  selector: 'app-column-filter',
  standalone: true,
  imports: [FormsModule, MatIconModule],
  template: `
    @if (spec(); as s) {
      <div class="cell">
        @switch (s.kind) {
          @case ('select') {
            <select [ngModel]="value()" (ngModelChange)="set($event)" [attr.aria-label]="label(s)">
              <option value="">{{ s.placeholder ?? 'Any' }}</option>
              @for (o of s.options ?? []; track o.value) {
                <option [value]="o.value">{{ o.label }}</option>
              }
            </select>
          }
          @case ('date') {
            <input type="date" [ngModel]="value()" (ngModelChange)="set($event)"
                   [attr.aria-label]="label(s)" />
          }
          @default {
            <input type="text" [ngModel]="value()" (ngModelChange)="set($event)"
                   [placeholder]="s.placeholder ?? 'Filter'" [attr.aria-label]="label(s)" />
          }
        }

        @if (value()) {
          <button type="button" class="clear" (click)="set('')" aria-label="Clear this filter">
            <mat-icon>close</mat-icon>
          </button>
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
     * scroll — which the table-scroll wrapper already handles — rather than crush their neighbours.
     */
    .cell { position: relative; display: block; min-width: 108px; }
    :host([data-kind='date']) .cell { min-width: 138px; }

    input, select {
      width: 100%; box-sizing: border-box;
      padding: 4px 22px 4px 7px;
      font: inherit; font-size: 12.5px; font-weight: 400;
      color: var(--text); background: var(--surface);
      border: 1px solid var(--border); border-radius: 6px;
    }
    input:focus, select:focus { outline: 2px solid #1d69d4; outline-offset: -1px; border-color: #1d69d4; }
    input::placeholder { color: var(--text-muted); }
    select { padding-right: 18px; cursor: pointer; }
    .clear {
      position: absolute; right: 2px; top: 50%; transform: translateY(-50%);
      display: grid; place-items: center;
      width: 18px; height: 18px; padding: 0;
      border: 0; background: none; cursor: pointer; color: var(--text-muted);
    }
    .clear mat-icon { font-size: 14px; width: 14px; height: 14px; }
  `,
})
export class ColumnFilterComponent {
  /** Lets the stylesheet give a date input the extra room its picker needs. */
  @HostBinding('attr.data-kind')
  get kind(): string | null { return this.spec()?.kind ?? null; }

  readonly spec = input<ColumnFilterSpec | undefined>();
  readonly value = input<string>('');
  readonly changed = output<string>();

  label = (s: ColumnFilterSpec) => `Filter by ${s.placeholder ?? s.key}`;

  set(value: string): void {
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
 * The filter row's state, shared by every grid that has one.
 *
 * Typing is debounced and a select is not: waiting 300ms after a dropdown choice is latency for
 * nothing, whereas firing a request per keystroke is a request per keystroke. Both funnel into one
 * `changes` stream so the grid has a single place to reload from.
 *
 * `params` is what goes on the wire. Empty values are dropped rather than sent as `col[x]=`, so a
 * cleared filter looks to the server exactly like one that was never set.
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
