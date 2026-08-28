import { Injectable, inject } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

/**
 * Where the reader just came from, inside this app.
 *
 * A detail screen has a natural parent — a task belongs under Tasks — but that is not always where
 * somebody arrived from: a coordinator reaches the same task from the assignment queue, a checker
 * from the Quality page, and sending all three back to Tasks is only right for one of them.
 *
 * The browser's own history cannot answer this on its own. `history.back()` walks out of the app
 * entirely when the detail page was the first thing loaded (a bookmark, a notification link, a
 * pasted URL), which is precisely when a back control is most needed and least safe.
 *
 * So this records the previous in-app URL. `app-back-link` uses it when there is one and falls back
 * to the parent list when there is not.
 */
@Injectable({ providedIn: 'root' })
export class NavigationHistory {
  private readonly router = inject(Router);

  private previousUrl: string | null = null;
  private currentUrl: string | null = null;

  constructor() {
    this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe((e) => {
        // Only when the path actually changes. Tabs and grid filters write to the query string on
        // every interaction, and treating those as navigation would make "back" mean "undo the
        // last thing I typed".
        const next = path(e.urlAfterRedirects);
        if (next === this.currentUrl) return;

        this.previousUrl = this.currentUrl;
        this.currentUrl = next;
      });
  }

  /** The last in-app page, or null when this is the first page of the session. */
  previous(): string | null {
    return this.previousUrl;
  }
}

function path(url: string): string {
  return url.split('?')[0].split('#')[0];
}
