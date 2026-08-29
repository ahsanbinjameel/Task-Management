import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, Observable, catchError, filter, switchMap, take, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { ApiProblem } from './models';
import { HANDLED_LOCALLY, looksTechnical, messageForStatus } from './form-errors';
import { ToastService } from './toast.service';

/** Endpoints that must not carry a token, and must not trigger a refresh when they 401. */
const ANONYMOUS = ['/api/auth/login', '/api/auth/refresh', '/api/auth/logout'];

const isAnonymous = (req: HttpRequest<unknown>) => ANONYMOUS.some((u) => req.url.startsWith(u));

/**
 * Serialises token refresh. Without this, a screen that fires six parallel requests would send six
 * refreshes on expiry — and because the server rotates refresh tokens and treats a replayed one as
 * theft, five of them would be rejected and the whole family revoked. The first 401 refreshes; the
 * rest queue on the result.
 */
let refreshInFlight = false;
const refreshed = new BehaviorSubject<string | null>(null);

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const authorised = isAnonymous(req) ? req : withToken(req, auth.accessToken);

  return next(authorised).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || isAnonymous(req)) {
        return throwError(() => error);
      }

      // A demo session has no refresh token, deliberately — see DemoController. So the honest
      // response to a 401 inside one is to end the demonstration and hand the operator their own
      // account back, not to sign them out of it. Signing somebody out because a *demo* token
      // aged out would lose their real session for a reason that has nothing to do with it.
      if (auth.inDemoMode()) {
        auth.restoreLiveSession();
        void router.navigate(['/']);
        return throwError(() => error);
      }

      if (!auth.refreshToken) {
        auth.clear();
        void router.navigate(['/login']);
        return throwError(() => error);
      }

      if (refreshInFlight) {
        return refreshed.pipe(
          filter((token): token is string => token !== null),
          take(1),
          switchMap((token) => next(withToken(req, token))),
        );
      }

      refreshInFlight = true;
      refreshed.next(null);

      return auth.refresh().pipe(
        switchMap((response) => {
          refreshInFlight = false;
          refreshed.next(response.accessToken);
          return next(withToken(req, response.accessToken));
        }),
        catchError((refreshError: unknown) => {
          refreshInFlight = false;
          auth.clear();
          void router.navigate(['/login']);
          return throwError(() => refreshError);
        }),
      );
    }),
  );
};

/**
 * Turns a ProblemDetails response into a toast, once, at the edge — so no component has to
 * remember to. 401 is excluded: the interceptor above is already handling it, and a "session
 * expired" toast during a successful silent refresh would be a lie.
 *
 * Requests marked `HANDLED_LOCALLY` are skipped. A form that shows its own inline errors would
 * otherwise report the same failure twice, once beside the field and once in a toast floating
 * outside the dialog.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);

  return next(req).pipe(
    catchError((error: unknown) => {
      const handledByCaller = req.context.get(HANDLED_LOCALLY);

      if (error instanceof HttpErrorResponse && error.status !== 401 && !handledByCaller) {
        toast.error(describe(error));
      }
      return throwError(() => error);
    }),
  ) as Observable<never>;
};

function withToken(req: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
}

/**
 * Prefers the server's own message, and falls back to plain language rather than a status code.
 *
 * Nothing here may surface a number, an exception name or a constraint name: those go to the log,
 * where someone who can act on them will look. What reaches the screen has to say what happened
 * and what to do next.
 */
export function describe(error: HttpErrorResponse): string {
  if (error.status === 0) {
    return 'Cannot reach the system right now. Check your connection and try again.';
  }

  const problem = error.error as ApiProblem | string | null;

  if (typeof problem === 'string' && problem.trim() && !looksTechnical(problem)) return problem;

  if (problem && typeof problem === 'object') {
    const detail = problem.detail?.trim();
    if (detail && !looksTechnical(detail)) return detail;

    const title = problem.title?.trim();
    if (title && !looksTechnical(title)) return title;
  }

  return messageForStatus(error.status);
}



/** The stable error code, for the rare case a caller needs to branch on the reason. */
export function problemCode(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const problem = error.error as ApiProblem | null;
  return problem && typeof problem === 'object' ? (problem.code ?? null) : null;
}
