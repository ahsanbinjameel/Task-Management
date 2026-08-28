import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { AuthResponse, UserDto } from './models';

const ACCESS_TOKEN = 'wfa.access';
const REFRESH_TOKEN = 'wfa.refresh';
const USER = 'wfa.user';

/**
 * Session state for the SPA.
 *
 * The access token is short-lived and carries the user's permissions as claims, so a permission
 * change only takes effect on the next token issue. That is a deliberate server-side trade-off; the
 * consequence here is that after a role change the user must sign out and back in, and the UI says
 * so rather than pretending otherwise.
 *
 * Tokens live in localStorage. That is the right call for bearer-token auth in a SPA — the token is
 * never sent automatically by the browser, which is exactly why the API needs no CSRF protection.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly _user = signal<UserDto | null>(readStoredUser());
  private readonly _permissions = signal<ReadonlySet<string>>(
    new Set(readStoredUser()?.permissions ?? []),
  );

  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => this._user() !== null);
  readonly displayName = computed(() => this._user()?.displayName ?? '');

  /**
   * The real human, when an administrator is acting as somebody else.
   *
   * Read from the access token rather than kept as its own flag. The token is the only thing the
   * server actually believes, so a separate client-side "I am pretending" boolean could disagree
   * with it — and the one state where the banner must never be wrong is this one.
   */
  private readonly _actingFor = signal<string | null>(actingForFromToken());
  readonly actingFor = this._actingFor.asReadonly();
  readonly isActingAsSomeoneElse = computed(() => this._actingFor() !== null);

  /** True when the account is on the clock and should see shift controls at all. */
  readonly tracksShift = computed(() => this.has('Workforce.TrackShift'));

  /**
   * True when this account only asks for work — it neither does it, coordinates it, reviews it nor
   * checks it. Mirrors `StatusViews.AudienceFor` on the server, which is what decides how much of
   * a record they are actually sent. The client hides the panels; the server empties them. Both,
   * because one without the other is either a lie or a leak.
   */
  readonly isRequesterOnly = computed(() => !this.hasAny(
    'Task.Work', 'Task.Assign', 'Task.Review', 'Task.Approve',
    'Task.QCReview', 'Task.Close', 'Dashboard.Management', 'Reports.View',
    'Request.ViewAll', 'Workforce.ViewAll'));

  get accessToken(): string | null {
    return localStorage.getItem(ACCESS_TOKEN);
  }

  get refreshToken(): string | null {
    return localStorage.getItem(REFRESH_TOKEN);
  }

  has(permission: string): boolean {
    return this._permissions().has(permission);
  }

  hasAny(...permissions: string[]): boolean {
    return permissions.some((p) => this._permissions().has(p));
  }

  /**
   * Start acting as somebody else.
   *
   * The response is an ordinary session for that person, so it goes through `store` like any
   * other — the whole point is that from here on the app is theirs, with their permissions and
   * nobody else's. What comes back on the token, and only there, is who is really behind it.
   */
  impersonate(userId: number, context?: HttpContext): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/impersonate', { userId }, { context })
      .pipe(tap((response) => this.store(response)));
  }

  /** Hand the administrator their own session back. */
  stopImpersonating(): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/stop-impersonating', {})
      .pipe(tap((response) => this.store(response)));
  }

  login(userName: string, password: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/login', { userName, password })
      .pipe(tap((response) => this.store(response)));
  }

  /**
   * Exchanges the refresh token for a new pair. The server rotates on every use and revokes the
   * whole family if an old token is replayed, so this must never run concurrently with itself —
   * the interceptor serialises it.
   */
  refresh(): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>('/api/auth/refresh', { refreshToken: this.refreshToken ?? '' })
      .pipe(tap((response) => this.store(response)));
  }

  logout(): void {
    const token = this.refreshToken;

    // Best effort: revoke server-side, but clear locally regardless. A failed logout call must
    // never leave the user apparently still signed in.
    if (token) {
      this.http.post('/api/auth/logout', { refreshToken: token }).subscribe({
        error: () => undefined,
      });
    }

    this.clear();
    void this.router.navigate(['/login']);
  }

  /** Re-reads the profile, e.g. after changing your own password. */
  refreshProfile(): Observable<UserDto> {
    return this.http.get<UserDto>('/api/auth/me').pipe(tap((user) => this.setUser(user)));
  }

  store(response: AuthResponse): void {
    localStorage.setItem(ACCESS_TOKEN, response.accessToken);
    localStorage.setItem(REFRESH_TOKEN, response.refreshToken);
    this.setUser(response.user);
    this._actingFor.set(actingForFromToken());
  }

  clear(): void {
    localStorage.removeItem(ACCESS_TOKEN);
    localStorage.removeItem(REFRESH_TOKEN);
    localStorage.removeItem(USER);
    this._actingFor.set(null);
    this._user.set(null);
    this._permissions.set(new Set());
  }

  /**
   * Updates only the fields someone can change about themselves, keeping everything else.
   *
   * Deliberately not `setUser`: the profile endpoint returns a `UserDto` with an **empty**
   * permission list (list projections omit permissions — they are only meaningful for one user and
   * would multiply the query cost). Passing that through `setUser` would clear every permission in
   * the session and blank the entire nav until the next sign-in.
   */
  applyProfile(user: Pick<UserDto, 'displayName' | 'email'>): void {
    const current = this._user();
    if (!current) return;

    const merged: UserDto = { ...current, displayName: user.displayName, email: user.email };
    localStorage.setItem(USER, JSON.stringify(merged));
    this._user.set(merged);
  }

  private setUser(user: UserDto): void {
    localStorage.setItem(USER, JSON.stringify(user));
    this._user.set(user);
    this._permissions.set(new Set(user.permissions));
  }
}

function readStoredUser(): UserDto | null {
  const raw = localStorage.getItem(USER);
  if (!raw) return null;

  try {
    return JSON.parse(raw) as UserDto;
  } catch {
    // Corrupt storage should log the user out, not crash the app on boot.
    localStorage.removeItem(USER);
    return null;
  }
}

/**
 * The display name of the real human, from the access token's own claim.
 *
 * A JWT payload is base64url, not base64, and may be unpadded — hence the two substitutions and
 * the padding. Nothing here is trusted for authorisation: it decides what a banner says, and the
 * server checks every call regardless. Any malformed token simply reads as "not acting", which is
 * the safe way round for a banner.
 */
function actingForFromToken(): string | null {
  const token = localStorage.getItem(ACCESS_TOKEN);
  if (!token) return null;

  const payload = token.split('.')[1];
  if (!payload) return null;

  try {
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/').padEnd(
      payload.length + ((4 - (payload.length % 4)) % 4), '='));
    const claims = JSON.parse(json) as Record<string, unknown>;

    if (claims['impersonated_by'] === undefined) return null;
    return (claims['impersonated_by_name'] as string) ?? 'an administrator';
  } catch {
    return null;
  }
}
