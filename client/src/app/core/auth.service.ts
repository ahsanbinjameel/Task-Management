import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
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

  /** True when the account is on the clock and should see shift controls at all. */
  readonly tracksShift = computed(() => this.has('Workforce.TrackShift'));

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
  }

  clear(): void {
    localStorage.removeItem(ACCESS_TOKEN);
    localStorage.removeItem(REFRESH_TOKEN);
    localStorage.removeItem(USER);
    this._user.set(null);
    this._permissions.set(new Set());
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
