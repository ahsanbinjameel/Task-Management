import { signal } from '@angular/core';
import { Observable } from 'rxjs';
import { SubmitFailure, describeFailure, handledLocally } from './form-errors';

/**
 * Submit state for any form — a dialog or an inline panel.
 *
 * The rule this exists to enforce: **a failed submit changes nothing except the error shown.** No
 * closing, no clearing, no navigating. Callers pass what to do on success; everything else is
 * handled here so that no individual form has to remember the policy.
 *
 * Requests are marked `handledLocally()`, so the global toast interceptor stays quiet and the
 * message appears where the user is looking instead of floating outside the form.
 *
 * Usage:
 *
 *     readonly form = new FormSubmit();
 *     ...
 *     save() {
 *       this.form.run(
 *         (ctx) => this.api.createRequest(body, ctx),
 *         (created) => this.ref.close(created),
 *       );
 *     }
 */
export class FormSubmit {
  readonly busy = signal(false);
  readonly failure = signal<SubmitFailure | null>(null);

  /** Message for one field, or null. Field names are lower-cased. */
  fieldError = (name: string): string | null =>
    this.failure()?.fields[name.toLowerCase()] ?? null;

  /** The form-level message, shown at the top. */
  message = (): string | null => this.failure()?.message ?? null;

  /** True once a submit has failed — use to reveal the error banner. */
  failed = (): boolean => this.failure() !== null;

  /**
   * Drops one field's error as soon as the user edits it, so a corrected field stops complaining
   * while the others keep their messages.
   */
  clearField(name: string): void {
    const key = name.toLowerCase();
    const current = this.failure();
    if (!current?.fields[key]) return;

    const remaining = { ...current.fields };
    delete remaining[key];
    this.failure.set({ ...current, fields: remaining });
  }

  reset(): void {
    this.failure.set(null);
    this.busy.set(false);
  }

  /**
   * Runs a submit. `request` receives the HttpContext that suppresses the global toast — pass it
   * straight through to the ApiService call.
   */
  run<T>(
    request: (context: ReturnType<typeof handledLocally>) => Observable<T>,
    onSuccess: (value: T) => void,
    options: { focusFirstInvalid?: boolean } = {},
  ): void {
    if (this.busy()) return;

    this.busy.set(true);
    this.failure.set(null);

    request(handledLocally()).subscribe({
      next: (value) => {
        this.busy.set(false);
        onSuccess(value);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.failure.set(describeFailure(error));

        if (options.focusFirstInvalid !== false) this.focusFirstInvalid();
      },
    });
  }

  private focusFirstInvalid(): void {
    const first = Object.keys(this.failure()?.fields ?? {})[0];
    if (!first) return;

    queueMicrotask(() => {
      document.querySelector<HTMLElement>('[name="' + first + '"]')?.focus();
    });
  }
}
