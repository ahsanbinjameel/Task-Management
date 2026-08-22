import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, Observable, catchError, filter, switchMap, take, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { ApiProblem } from './models';
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
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status !== 401) {
        toast.error(describe(error));
      }
      return throwError(() => error);
    }),
  ) as Observable<never>;
};

function withToken(req: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
}

/** Prefers the server's own message; falls back to something honest about what went wrong. */
export function describe(error: HttpErrorResponse): string {
  if (error.status === 0) {
    return 'Cannot reach the server. Check that the API is running.';
  }

  const problem = error.error as ApiProblem | string | null;

  if (typeof problem === 'string' && problem.trim()) return problem;

  if (problem && typeof problem === 'object') {
    const detail = problem.detail?.trim();
    if (detail) return detail;

    const title = problem.title?.trim();
    if (title) return title;
  }

  return error.status === 403
    ? 'You do not have permission to do that.'
    : `Request failed (${error.status}).`;
}

/** The stable error code, for the rare case a caller needs to branch on the reason. */
export function problemCode(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const problem = error.error as ApiProblem | null;
  return problem && typeof problem === 'object' ? (problem.code ?? null) : null;
}
