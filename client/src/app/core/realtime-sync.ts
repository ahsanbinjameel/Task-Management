import { DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, debounceTime, filter, merge } from 'rxjs';

/**
 * Keeps a screen in step with the server.
 *
 * Three rules are baked in so no screen has to remember them:
 *
 * 1. **Re-fetch, never patch.** Events carry an id and a status, not a record. The screen reloads
 *    from the database, which is what keeps it correct when events arrive out of order or one is
 *    dropped during a reconnect.
 * 2. **Coalesce.** Approving a request can create a task and assign it in one save, which is three
 *    events. Without debouncing that is three reloads of the same screen. A short window collapses
 *    a burst into one refresh, which also absorbs duplicate deliveries after a reconnect.
 * 3. **Tear down.** These are long-lived root-scoped subjects. Without `takeUntilDestroyed`, every
 *    visit to a screen leaves another live subscription behind and each event triggers one more
 *    reload than the last — the leak that made the app feel progressively slower.
 *
 * `filter` narrows to events the screen actually cares about, so a task screen does not reload for
 * an unrelated task.
 */
export function syncOn<T = unknown>(
  streams: readonly Observable<unknown>[],
  reload: () => void,
  destroyRef: DestroyRef,
  options: { debounce?: number; filter?: (event: T) => boolean } = {},
): void {
  if (streams.length === 0) return;

  const keep = options.filter;

  merge(...streams)
    .pipe(
      filter((event) => (keep ? keep(event as T) : true)),
      debounceTime(options.debounce ?? 250),
      takeUntilDestroyed(destroyRef),
    )
    .subscribe(() => reload());
}
