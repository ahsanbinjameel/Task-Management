import { HttpContext, HttpContextToken, HttpErrorResponse } from '@angular/common/http';
import { ApiProblem } from './models';

/**
 * Marks a request whose failure the caller will show itself.
 *
 * Without this, a form that renders its own inline errors also triggers the global toast from
 * `errorInterceptor`, so the user is told the same thing twice in two places — which is the
 * complaint that started this: the message appearing *outside* the form.
 */
export const HANDLED_LOCALLY = new HttpContextToken<boolean>(() => false);

/** Convenience for call sites: `this.api.createUser(body, handledLocally())`. */
export const handledLocally = (): HttpContext =>
  new HttpContext().set(HANDLED_LOCALLY, true);

export interface SubmitFailure {
  /** Shown at the top of the form. Always populated. */
  message: string;
  /** Field name (lower-cased) → message, for rendering beside the offending control. */
  fields: Record<string, string>;
  /** The server's stable error code, when it sent one. */
  code: string | null;
}

/**
 * Turns any failed response into something a form can render.
 *
 * Two shapes arrive from this API:
 *
 * - **Model validation** — ASP.NET's `ValidationProblemDetails`, with an `errors` dictionary keyed
 *   by property name. Those map straight onto fields.
 * - **Business rules** — our own `ProblemDetails` with a stable `code` and a human `detail`. Those
 *   have no field, so a few well-known codes are mapped onto the control they are really about
 *   (a taken username belongs next to the username box, not floating above the form).
 */
export function describeFailure(error: unknown): SubmitFailure {
  if (!(error instanceof HttpErrorResponse)) {
    return { message: 'Something went wrong. Please try again.', fields: {}, code: null };
  }

  if (error.status === 0) {
    return {
      message: 'Cannot reach the server. Check your connection and try again.',
      fields: {},
      code: null,
    };
  }

  const problem = (error.error ?? null) as (ApiProblem & { errors?: Record<string, string[]> }) | null;
  const fields: Record<string, string> = {};

  if (problem?.errors) {
    for (const [key, messages] of Object.entries(problem.errors)) {
      if (messages?.length) fields[key.toLowerCase()] = messages[0];
    }
  }

  const code = problem?.code ?? null;
  const detail = problem?.detail?.trim() || problem?.title?.trim() || '';

  // Business-rule failures carry no field, but the user still needs to see them in context.
  const field = code ? CODE_TO_FIELD[code] : undefined;
  if (field && detail && !fields[field]) fields[field] = detail;

  const message = (detail && !looksTechnical(detail) ? detail : '')
    || Object.values(fields)[0]
    || messageForStatus(error.status);

  return { message, fields, code };
}

/** Business-rule codes that are really about one control. */
const CODE_TO_FIELD: Record<string, string> = {
  'user.username_taken': 'username',
  'user.email_taken': 'email',
  'auth.password_too_weak': 'password',
  'auth.password_policy': 'password',
};

/** True when the failure was a validation problem rather than something unexpected. */
export const isValidationFailure = (failure: SubmitFailure): boolean =>
  Object.keys(failure.fields).length > 0;

/** Plain-language fallbacks, so a bare status code never reaches the screen. */
export function messageForStatus(status: number): string {
  if (status === 400) return 'Some of the details were not accepted. Please check them and try again.';
  if (status === 401) return 'Please sign in again.';
  if (status === 403) return 'You do not have permission to do that.';
  if (status === 404) return 'That item could not be found. It may have been removed.';
  if (status === 409) return 'Someone else changed this first. Refresh the page and try again.';
  if (status === 413) return 'That file is too large to upload.';
  if (status === 429) return 'Too many attempts. Please wait a moment and try again.';
  if (status >= 500) {
    return 'Something went wrong at our end. Please try again. '
      + 'If it keeps happening, contact your administrator.';
  }
  return 'That did not work. Please try again.';
}

/**
 * Guards against a stack trace, a constraint name, or a literal "undefined" leaking through as if
 * it were a sentence written for a person. Those belong in the log, not on screen.
 */
export function looksTechnical(text: string): boolean {
  const t = text.trim();
  if (!t || t === 'undefined' || t === 'null' || t === '[object Object]') return true;

  return /System\.[A-Z]|Exception|at [A-Z][\w.]+\(|SqlException|IX_|FK_|UX_|Microsoft\.|--->/.test(t);
}
