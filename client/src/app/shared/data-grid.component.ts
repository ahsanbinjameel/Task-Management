import {
  Component, Directive, TemplateRef, computed, contentChildren, inject, input, output,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { SortHeaderComponent, SortState } from './sort-header.component';
import { EmptyComponent, LoadingComponent } from './ui';
import {
  ColumnFilterComponent, ColumnFilterSpec, ColumnFilterState, FilterSummaryComponent,
  NoMatchesComponent,
} from './column-filter.component';

/** Several values for one column travel pipe-separated. Mirrors `ColumnFilters.Many` server-side. */
const SEPARATOR = '|';

/**
 * One column of a grid.
 *
 * A column is a *declaration*, not markup: the grid renders the heading, the sort control, the
 * filter cell and the body cell from this one object. Cells that need more than text supply an
 * `<ng-template gridCell="key">` and keep everything else.
 */
export interface GridColumn<T = any> {
  /** Identifies the column, the filter key sent as `col[key]`, and the `gridCell` template name. */
  key: string;
  header: string;
  /** Makes the heading clickable. The parent sorts (server mode) or the grid does (local mode). */
  sortable?: boolean;
  /** The filter control for this column. `key` is filled in from the column, never repeated. */
  filter?: Omit<ColumnFilterSpec, 'key'>;
  /** Text for a cell with no template. Also the fallback for local filtering and sorting. */
  cell?: (row: T) => string | number | null | undefined;
  /** Overrides `cell` when the value read differs from the one printed (an id, a raw date). */
  filterValue?: (row: T) => string | number | null | undefined;
  /** Overrides `cell` for ordering — return a number to sort by rank rather than alphabetically. */
  sortValue?: (row: T) => string | number | null | undefined;
  /** Extra classes on the body cell, e.g. `mono nowrap`. */
  cellClass?: string;
  align?: 'right';
  /** Floor for the whole column. The filter row sets it, so it is declared once, here. */
  minWidth?: number;
  /** Leaves the heading blank — for an actions or quick-look column. */
  headerHidden?: boolean;
}

/**
 * The body cell for one column.
 *
 * `<ng-template gridCell="title" let-row let-i="index">`. Anything beyond plain text — a link, a
 * chip, a menu, a progress bar — arrives this way, so the grid stays responsible for the table and
 * the screen stays responsible for its own content.
 */
@Directive({ selector: '[gridCell]', standalone: true })
export class GridCellDirective {
  readonly gridCell = input.required<string>();
  readonly template = inject(TemplateRef<unknown>);
}

/**
 * **The** grid. Every list in the application is one of these.
 *
 * It exists because seven screens had independently grown the same table: a scroll wrapper, a
 * header row, a generated filter row, sortable headings, a dimmed reload, an empty state, a
 * "nothing matches" strip that keeps the filter row reachable, and a paginator — each with its own
 * copy of the CSS and its own chance to drift. All of it is here once. A screen supplies its
 * columns and its cell templates; it does not supply a table.
 *
 * Columns stay fully dynamic: `columns` is a signal input, so a grid whose shape depends on the
 * view being looked at, or on the reader's permissions, simply recomputes the array.
 *
 * **Two modes.** `server` (the default) leaves filtering, sorting and paging to the API — right for
 * anything paged, because narrowing the page you happen to be on would report "2 matches" out of
 * thirty. `local` does both in place, which is right exactly when the whole set arrived in one
 * call, and only then.
 */
@Component({
  selector: 'app-data-grid',
  standalone: true,
  imports: [
    NgTemplateOutlet, MatTableModule, MatPaginatorModule, SortHeaderComponent,
    ColumnFilterComponent, FilterSummaryComponent, NoMatchesComponent,
    EmptyComponent, LoadingComponent,
  ],
  template: `
    @if (filters()) {
      <app-filter-summary [count]="filters()!.activeCount()" (clear)="clearFilters()" />
    }

    <div class="card grid-card" [class.refreshing]="refreshing()">
      @if (loading()) {
        <app-loading />
      } @else if (rows().length === 0 && !narrowed()) {
        <app-empty [message]="emptyMessage()" [icon]="emptyIcon()"
                   [actionLabel]="emptyActionLabel()" [actionRoute]="emptyActionRoute()" />
      } @else {
        <div class="table-scroll">
          <table mat-table [dataSource]="visible()">
            @for (column of columns(); track column.key) {
              <ng-container [matColumnDef]="column.key">
                <th mat-header-cell *matHeaderCellDef
                    [class.right]="column.align === 'right'"
                    [style.min-width.px]="column.minWidth ?? null"
                    [attr.aria-label]="column.headerHidden ? column.header : null">
                  @if (column.headerHidden) {
                    <!--
                      Nothing rendered; the name goes on the cell as aria-label instead.

                      A visually-hidden span is the usual way to do this and is wrong here:
                      position absolute with no positioned ancestor makes the initial containing
                      block its container, so the span escapes the table-scroll wrapper's clipping.
                      In a table wide enough to scroll, an actions column sits hundreds of pixels
                      to the right — and the escaped span pushed the whole page that far sideways
                      at 480px, breaking the one layout rule this app has.
                    -->
                  } @else if (column.sortable) {
                    <app-sort-header [label]="column.header" [column]="column.key"
                                     [sort]="sort()" (sortChange)="sortChange.emit($event)" />
                  } @else {
                    {{ column.header }}
                  }
                </th>
                <td mat-cell *matCellDef="let row; let i = index"
                    [class]="column.cellClass ?? ''"
                    [class.right]="column.align === 'right'">
                  @if (templateFor(column.key); as tpl) {
                    <ng-container
                      [ngTemplateOutlet]="tpl"
                      [ngTemplateOutletContext]="{ $implicit: row, row: row, index: i }" />
                  } @else {
                    {{ text(column, row) }}
                  }
                </td>
              </ng-container>

              @if (filters()) {
                <ng-container [matColumnDef]="column.key + '_filter'">
                  <th mat-header-cell *matHeaderCellDef class="filter-cell">
                    <app-column-filter
                      [spec]="specFor(column)"
                      [value]="filters()!.value(column.key)"
                      (changed)="filters()!.set(specFor(column), column.key, $event)" />
                  </th>
                </ng-container>
              }
            }

            <tr mat-header-row *matHeaderRowDef="keys()"></tr>
            @if (filters()) {
              <tr mat-header-row *matHeaderRowDef="filterKeys()" class="filter-row"></tr>
            }
            <tr mat-row *matRowDef="let row; columns: keys()"
                [class.clickable]="clickable()"
                [attr.tabindex]="clickable() ? 0 : null"
                (click)="click(row)"
                (keydown.enter)="click(row)"></tr>
          </table>
        </div>

        <!--
          The table stays on screen when nothing matches. Replacing it with an empty state would
          take the filter row with it — removing the one control that can undo the problem.
        -->
        @if (visible().length === 0) {
          <app-no-matches [message]="noMatchesMessage()" (clear)="clearFilters()" />
        } @else if (total() !== null) {
          <mat-paginator [length]="total()!" [pageSize]="pageSize()" [pageIndex]="pageIndex()"
                         [pageSizeOptions]="pageSizeOptions()" (page)="pageChange.emit($event)" />
        }
      }
    </div>
  `,
  styles: `
    /*
     * A filter reload dims the rows rather than replacing the table. Deliberately not the whole
     * card and deliberately no pointer-events block: the filter row is what triggered the reload,
     * and freezing it would stop the next keystroke landing.
     */
    .refreshing tbody { opacity: .45; transition: opacity .12s; }
    .grid-card { overflow: hidden; }
  `,
})
export class DataGridComponent<T = any> {
  readonly rows = input.required<readonly T[]>();
  readonly columns = input.required<readonly GridColumn<T>[]>();

  /** Filtering and ordering: `server` asks the API, `local` does it here. See the class note. */
  readonly mode = input<'server' | 'local'>('server');

  /** Left null and no filter row is rendered at all. */
  readonly filters = input<ColumnFilterState | null>(null);
  /**
   * What each column can still be narrowed by, given the *other* columns — from the server's
   * filter-options call. Absent means "not known yet", and everything stays on offer.
   */
  readonly filterOptions = input<Record<string, string[]> | null>(null);
  /**
   * True when something outside the filter row is already narrowing the grid — a status tile.
   * It makes an empty result read as "nothing matches", which offers a way back, rather than as
   * "there is nothing here", which does not.
   */
  readonly externalFilter = input(false);

  readonly sort = input<SortState>({ by: null, descending: true });
  readonly sortChange = output<SortState>();

  readonly loading = input(false);
  /** A reload that must not unmount the table — see the note on `.refreshing`. */
  readonly refreshing = input(false);

  readonly emptyMessage = input('Nothing here yet');
  readonly emptyIcon = input('inbox');
  readonly emptyActionLabel = input<string>();
  readonly emptyActionRoute = input<string | unknown[]>();
  readonly noMatchesMessage = input('Nothing matches those filters.');

  /** Null hides the paginator — right for a grid that arrived whole. */
  readonly total = input<number | null>(null);
  readonly pageSize = input(25);
  readonly pageIndex = input(0);
  readonly pageSizeOptions = input<number[]>([25, 50, 100]);
  readonly pageChange = output<PageEvent>();

  readonly clickable = input(false);
  readonly rowClick = output<T>();
  /** Fires alongside the filter row being cleared, for a screen that also holds a tile. */
  readonly clearedFilters = output<void>();

  private readonly cells = contentChildren(GridCellDirective);

  readonly keys = computed(() => this.columns().map((c) => c.key));
  readonly filterKeys = computed(() => this.keys().map((k) => k + '_filter'));

  templateFor(key: string): TemplateRef<unknown> | null {
    return this.cells().find((c) => c.gridCell() === key)?.template ?? null;
  }

  /**
   * The filter spec for a column: what the column declared, plus the key it is already identified
   * by and whatever the server says is still reachable. Written here so no screen repeats either.
   */
  specFor(column: GridColumn<T>): ColumnFilterSpec | undefined {
    if (!column.filter) return undefined;

    const available = this.filterOptions()?.[column.key];
    return {
      ...column.filter,
      key: column.key,
      minWidth: column.filter.minWidth ?? column.minWidth,
      available: available ? new Set(available) : column.filter.available,
    };
  }

  text(column: GridColumn<T>, row: T): string {
    const value = column.cell?.(row);
    return value === null || value === undefined || value === '' ? '—' : String(value);
  }

  click(row: T): void {
    if (this.clickable()) this.rowClick.emit(row);
  }

  clearFilters(): void {
    this.filters()?.clear();
    this.clearedFilters.emit();
  }

  /** Anything narrowing the grid, from either the filter row or a tile above it. */
  readonly narrowed = computed(() => this.externalFilter() || (this.filters()?.any() ?? false));

  /**
   * The rows actually rendered. In server mode that is exactly what was handed in — the API has
   * already narrowed and ordered it. In local mode the whole set is here, so doing it in place
   * cannot misreport a total.
   */
  readonly visible = computed<readonly T[]>(() => {
    if (this.mode() === 'server') return this.rows();

    const state = this.filters();
    const values = state ? state.current() : {};
    const byKey = new Map(this.columns().map((c) => [c.key, c]));

    const rows = this.rows().filter((row) =>
      Object.entries(values).every(([key, raw]) => !raw || matches(byKey.get(key), row, raw)));

    const { by, descending } = this.sort();
    const column = by ? byKey.get(by) : undefined;
    if (!column) return rows;

    const direction = descending ? -1 : 1;
    const read = column.sortValue ?? column.cell;
    // Copied first: `rows` may still be the input array, and sorting in place would mutate it.
    return [...rows].sort((a, b) => direction * compare(read?.(a), read?.(b)));
  });
}

/**
 * One column's filter against one row. Within a column several values are OR'd; across columns
 * they are AND'd — the rule the server applies, and the one people expect.
 */
function matches<T>(column: GridColumn<T> | undefined, row: T, raw: string): boolean {
  // A key nobody handles filters nothing, rather than emptying the grid.
  if (!column) return true;

  const read = column.filterValue ?? column.cell;
  const value = read?.(row);

  switch (column.filter?.kind) {
    // Free text is never split, so a term may legitimately contain the separator.
    case 'text': {
      const term = raw.trim().toLowerCase();
      // "-" asks for the rows nobody is on — the one answer with no name to type.
      if (term === '-') return value === null || value === undefined || value === '';
      return String(value ?? '').toLowerCase().includes(term);
    }

    // Local, not UTC: the column prints a local date, and filtering by a different day boundary
    // than the one on screen is the mismatch the business-calendar rule exists to avoid.
    case 'date':
      return value ? localDay(String(value)) === raw : false;

    default:
      return raw.split(SEPARATOR).some((wanted) => wanted === String(value ?? ''));
  }
}

function compare(a: unknown, b: unknown): number {
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  return String(a ?? '').localeCompare(String(b ?? ''));
}

/** `yyyy-MM-dd` in the browser's zone — the same day the column prints. */
function localDay(iso: string): string {
  const date = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
