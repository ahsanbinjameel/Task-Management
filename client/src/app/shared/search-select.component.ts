import {
  booleanAttribute, Component, computed, ElementRef, forwardRef, input, output, signal, viewChild,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { humanizeEnum } from '../core/labels';
import { ChipComponent } from './ui';

/** One row of a searchable dropdown. `chip` renders the tone-coded chip used elsewhere. */
export interface SelectOption {
  value: unknown;
  label: string;
  disabled?: boolean;
  /** Secondary text shown after the label — e.g. a task number or a role. */
  hint?: string;
  chip?: string;
  chipKind?: 'status' | 'priority' | 'workforce' | 'plain';
}

/** Enum-ish string lists — the common case — turned into options with humanised labels. */
export function enumOptions<T extends string>(
  values: readonly T[],
  label: (value: T) => string = (value) => humanizeEnum(value),
): SelectOption[] {
  return values.map((value) => ({ value, label: label(value) }));
}

/**
 * A dropdown you can type in.
 *
 * Replaces `mat-select` everywhere. A plain select is fine for four options and unusable for two
 * hundred people, and having two different controls for the same job means the user has to work
 * out which one they are looking at before they can use it — so there is only this one.
 *
 * The text box is a filter, never a value: typing narrows the list, and anything left unmatched is
 * discarded on blur. That keeps it a *select* — the value can only ever be one of the options —
 * while still letting someone find an entry by typing three letters of it.
 *
 * Works with `[(ngModel)]` and `formControlName` alike; `(valueChange)` is there for the screens
 * that reload a list when a filter changes.
 */
@Component({
  selector: 'app-search-select',
  standalone: true,
  imports: [
    MatFormFieldModule, MatInputModule, MatAutocompleteModule, MatChipsModule, MatIconModule,
    ChipComponent,
  ],
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => SearchSelectComponent),
    multi: true,
  }],
  template: `
    <mat-form-field class="field">
      @if (label()) { <mat-label>{{ label() }}</mat-label> }

      @if (multiple()) {
        <mat-chip-grid #grid [disabled]="isDisabled()" [required]="required()">
          @for (option of chosen(); track option) {
            <mat-chip-row [disabled]="isDisabled()" (removed)="deselect(option)">
              {{ option.label }}
              <button matChipRemove [attr.aria-label]="'Remove ' + option.label">
                <mat-icon>cancel</mat-icon>
              </button>
            </mat-chip-row>
          }
          <input #box [matChipInputFor]="grid" [matAutocomplete]="auto" autocomplete="off"
                 [placeholder]="placeholder()" [value]="text()" [disabled]="isDisabled()"
                 (input)="onType($event)" (focus)="onFocus()" (blur)="onBlur()" />
        </mat-chip-grid>
      } @else {
        <input #box matInput [matAutocomplete]="auto" autocomplete="off" role="combobox"
               [placeholder]="hintPlaceholder()" [value]="text()" [required]="required()"
               [disabled]="isDisabled()"
               (input)="onType($event)" (focus)="onFocus()" (blur)="onBlur()" />
      }

      <mat-icon matSuffix class="arrow" (click)="focusBox()">arrow_drop_down</mat-icon>

      <mat-autocomplete #auto [autoActiveFirstOption]="true" [displayWith]="display"
                        (optionSelected)="pick($event)">
        @for (option of visible(); track option) {
          <mat-option [value]="option" [disabled]="!!option.disabled">
            <span class="opt">
              @if (isChosen(option)) { <mat-icon class="tick">check</mat-icon> }
              <span class="text">{{ option.label }}</span>
              @if (option.chip) {
                <app-chip [value]="option.chip" [kind]="option.chipKind ?? 'plain'" />
              }
              @if (option.hint) { <span class="muted small">{{ option.hint }}</span> }
            </span>
          </mat-option>
        } @empty {
          <mat-option [disabled]="true">{{ emptyText() }}</mat-option>
        }
      </mat-autocomplete>
    </mat-form-field>
  `,
  styles: `
    :host { display: block; }
    .field { width: 100%; }
    .arrow { cursor: pointer; opacity: 0.7; }
    .opt { display: flex; align-items: center; gap: 8px; min-width: 0; }
    .opt .text { overflow: hidden; text-overflow: ellipsis; }
    .tick { font-size: 18px; width: 18px; height: 18px; flex: none; opacity: 0.75; }
  `,
})
export class SearchSelectComponent implements ControlValueAccessor {
  readonly label = input('');
  readonly options = input<readonly SelectOption[]>([]);
  readonly multiple = input(false, { transform: booleanAttribute });
  readonly required = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly placeholder = input('Type to search');
  /** Adds an explicit "no value" row — the "Any" / "Nobody" a filter or an optional field needs. */
  readonly nullLabel = input<string | null>(null);
  readonly emptyText = input('No matches');

  readonly valueChange = output<unknown>();

  private readonly box = viewChild<ElementRef<HTMLInputElement>>('box');

  /** `null` means "not typing" — the box then shows whatever is selected. */
  private readonly query = signal<string | null>(null);
  private readonly selected = signal<unknown>(null);
  private readonly selectedMany = signal<readonly unknown[]>([]);
  private readonly disabledByForm = signal(false);

  /** Set by `pick`, consumed by the focus Material fires immediately afterwards. */
  private justPicked = false;

  private propagate: (value: unknown) => void = () => {};
  private markTouched: () => void = () => {};

  readonly isDisabled = computed(() => this.disabled() || this.disabledByForm());

  private readonly rows = computed<readonly SelectOption[]>(() => {
    const label = this.nullLabel();
    if (label === null || this.multiple()) return this.options();
    return [{ value: null, label }, ...this.options()];
  });

  /** The rows the panel shows: filtered by what has been typed, minus anything already chosen. */
  readonly visible = computed(() => {
    const term = (this.query() ?? '').trim().toLowerCase();
    const taken = this.multiple() ? this.selectedMany() : [];
    return this.rows().filter((option) =>
      !taken.some((value) => same(value, option.value)) &&
      (term === '' ||
        option.label.toLowerCase().includes(term) ||
        (option.hint ?? '').toLowerCase().includes(term)));
  });

  /** In multiple mode, the chips. Unknown values are dropped — the options may still be loading. */
  readonly chosen = computed(() =>
    this.selectedMany()
      .map((value) => this.rows().find((option) => same(option.value, value)))
      .filter((option): option is SelectOption => option !== undefined));

  private readonly current = computed(() =>
    this.rows().find((option) => same(option.value, this.selected())));

  readonly text = computed(() =>
    this.query() ?? (this.multiple() ? '' : this.current()?.label ?? ''));

  /** While the box is being typed in it is empty, so the placeholder says what is still selected. */
  readonly hintPlaceholder = computed(() => this.current()?.label ?? this.placeholder());

  isChosen(option: SelectOption): boolean {
    return this.multiple()
      ? this.selectedMany().some((value) => same(value, option.value))
      : same(option.value, this.selected());
  }

  /**
   * Focusing empties the box so it can be typed into as a search field — the current value stays
   * on as the placeholder, and comes back on blur. Selecting the existing text instead would have
   * been prettier, but a mouse click lands the caret mid-word and the search then reads as gibberish.
   *
   * The exception is the focus Material fires straight after a selection: emptying the field there
   * would leave it looking blank at the exact moment someone chose something.
   */
  onFocus(): void {
    if (this.isDisabled()) return;
    if (this.justPicked) { this.justPicked = false; return; }
    this.query.set('');
  }

  /** Material renders the option object; without this the raw object reaches the text box. */
  display = (option: SelectOption | null): string => option?.label ?? '';

  onType(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }

  /** Typed text is a filter, not a value — whatever did not match is thrown away. */
  onBlur(): void {
    this.query.set(null);
    // Picking with the keyboard never blurs, so the guard can outlive its one focus. Leaving the
    // field is the point at which it has certainly done its job.
    this.justPicked = false;
    this.markTouched();
  }

  pick(event: MatAutocompleteSelectedEvent): void {
    const option = event.option.value as SelectOption;
    this.query.set(null);
    this.justPicked = true;
    if (this.multiple()) {
      this.commitMany([...this.selectedMany(), option.value]);
      this.focusBox();
    } else {
      this.commit(option.value);
    }

    // Material writes the chosen text straight into the DOM, behind Angular's back. Put the
    // box back in step by hand rather than hoping the next binding pass happens to differ.
    const box = this.box()?.nativeElement;
    if (box) box.value = this.text();
  }

  deselect(option: SelectOption): void {
    this.commitMany(this.selectedMany().filter((value) => !same(value, option.value)));
  }

  focusBox(): void {
    this.box()?.nativeElement.focus();
  }

  private commit(value: unknown): void {
    this.selected.set(value);
    this.propagate(value);
    this.valueChange.emit(value);
  }

  private commitMany(values: readonly unknown[]): void {
    this.selectedMany.set(values);
    const copy = [...values];
    this.propagate(copy);
    this.valueChange.emit(copy);
  }

  writeValue(value: unknown): void {
    if (this.multiple()) this.selectedMany.set(Array.isArray(value) ? value : []);
    else this.selected.set(value ?? null);
  }

  registerOnChange(fn: (value: unknown) => void): void { this.propagate = fn; }
  registerOnTouched(fn: () => void): void { this.markTouched = fn; }
  setDisabledState(disabled: boolean): void { this.disabledByForm.set(disabled); }
}

/** Null and undefined are the same absence — a cleared filter and an unset one must match. */
function same(a: unknown, b: unknown): boolean {
  return (a ?? null) === (b ?? null);
}
