import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, catchError, of, tap } from 'rxjs';
import { AuthResponse, DemoTokenDto, UserDto } from './models';

const ACCESS_TOKEN = 'wfa.access';
const REFRESH_TOKEN = 'wfa.refresh';
const USER = 'wfa.user';

/**
 * Where the real session waits while a demonstration runs.
 *
 * Demo mode does not sign anybody out — it puts a demo token in front of the real one and gives it
 * back afterwards. Keeping the real pair here rather than re-authenticating on the way out means
 * exiting cannot fail, which matters when it happens in front of an audience.
 */
const LIVE_ACCESS = 'wf.live.access';
const LIVE_REFRESH = 'wf.live.refresh';
const LIVE_USER = 'wf.live.user';

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
  private readonly _demoRealUser = signal<string | null>(demoRealUserFromToken());
  readonly demoRealUser = this._demoRealUser.asReadonly();
  readonly inDemoMode = computed(() => this._demoRealUser() !== null);

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
   * Start a demonstration, keeping the real session in reserve.
   *
   * The demo token replaces the live one for every subsequent request — which is what points them
   * at the demo database — while the live pair is set aside untouched. Nothing is signed out, so
   * nothing has to be signed back in.
   */
  enterDemo(demoUserId?: number): Observable<DemoTokenDto> {
    return this.http
      .post<DemoTokenDto>('/api/demo/enter', { demoUserId })
      .pipe(tap((response) => {
        this.keepLiveSession();
        this.applyDemoToken(response);
      }));
  }

  /** Change which of the cast is being shown. No sign-in, no sign-out. */
  switchDemoUser(demoUserId: number): Observable<DemoTokenDto> {
    return this.http
      .post<DemoTokenDto>('/api/demo/switch', { demoUserId })
      .pipe(tap((response) => this.applyDemoToken(response)));
  }

  /**
   * End the demonstration and restore the real session.
   *
   * The server is told first and best-effort — it records that a demonstration ended — but the
   * restore does not depend on it. A demonstration must always be escapable, including when the
   * network is not co-operating.
   */
  exitDemo(): Observable<unknown> {
    return this.http.post('/api/demo/exit', {}).pipe(
      catchError(() => of(null)),
      tap(() => this.restoreLiveSession()),
    );
  }

  /**
   * Put the real session back without asking the server.
   *
   * Also the answer to a demo token expiring: a demo session has no refresh token, deliberately, so
   * the honest response to a 401 inside one is to end the demonstration rather than sign the
   * operator out of their own account.
   */
  restoreLiveSession(): void {
    const access = localStorage.getItem(LIVE_ACCESS);
    const refresh = localStorage.getItem(LIVE_REFRESH);
    const user = localStorage.getItem(LIVE_USER);

    if (!access || !refresh || !user) {
      // Nothing to go back to — which should not happen, but signing out is the only honest
      // alternative to leaving somebody stranded in a session they cannot leave.
      this.logout();
      return;
    }

    localStorage.setItem(ACCESS_TOKEN, access);
    localStorage.setItem(REFRESH_TOKEN, refresh);
    localStorage.setItem(USER, user);

    localStorage.removeItem(LIVE_ACCESS);
    localStorage.removeItem(LIVE_REFRESH);
    localStorage.removeItem(LIVE_USER);

    const parsed = JSON.parse(user) as UserDto;
    this._user.set(parsed);
    this._permissions.set(new Set(parsed.permissions ?? []));
    this._demoRealUser.set(null);
  }

  private keepLiveSession(): void {
    const access = localStorage.getItem(ACCESS_TOKEN);
    const refresh = localStorage.getItem(REFRESH_TOKEN);
    const user = localStorage.getItem(USER);

    if (access) localStorage.setItem(LIVE_ACCESS, access);
    if (refresh) localStorage.setItem(LIVE_REFRESH, refresh);
    if (user) localStorage.setItem(LIVE_USER, user);
  }

  /**
   * A demo session has an access token and no refresh token — see DemoController for why that
   * absence is the safety property. The live refresh token is deliberately left in place so the
   * interceptor never tries to refresh a demo session into existence.
   */
  private applyDemoToken(response: DemoTokenDto): void {
    localStorage.setItem(ACCESS_TOKEN, response.accessToken);
    localStorage.setItem(USER, JSON.stringify(response.user));
    localStorage.removeItem(REFRESH_TOKEN);

    this._user.set(response.user);
    this._permissions.set(new Set(response.user.permissions ?? []));
    this._demoRealUser.set(demoRealUserFromToken());
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
    this._demoRealUser.set(demoRealUserFromToken());
  }

  clear(): void {
    localStorage.removeItem(ACCESS_TOKEN);
    localStorage.removeItem(REFRESH_TOKEN);
    localStorage.removeItem(USER);
    this._demoRealUser.set(null);
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
function demoRealUserFromToken(): string | null {
  const token = localStorage.getItem(ACCESS_TOKEN);
  if (!token) return null;

  const payload = token.split('.')[1];
  if (!payload) return null;

  try {
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/').padEnd(
      payload.length + ((4 - (payload.length % 4)) % 4), '='));
    const claims = JSON.parse(json) as Record<string, unknown>;

    if (claims['demo'] !== 'true' && claims['demo'] !== true) return null;
    return (claims['demo_real_user_name'] as string) ?? 'your own account';
  } catch {
    return null;
  }
}
