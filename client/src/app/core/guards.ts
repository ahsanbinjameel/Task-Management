import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { ToastService } from './toast.service';

/**
 * Requires a signed-in session. Remembers where the user was heading so a session that expires
 * mid-navigation resumes where it left off instead of dumping them on a dashboard.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) return true;

  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/**
 * Requires one of the given permissions. This is a routing convenience, not a security boundary —
 * the API checks again on every call. Its real job is to stop the app navigating somewhere that
 * would only render a wall of 403s.
 */
export function requirePermission(...permissions: string[]): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const toast = inject(ToastService);

    if (!auth.isAuthenticated()) return router.createUrlTree(['/login']);
    if (auth.hasAny(...permissions)) return true;

    toast.error('You do not have access to that area.');
    return router.createUrlTree(['/']);
  };
}
