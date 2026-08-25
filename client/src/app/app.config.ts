import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { MAT_FORM_FIELD_DEFAULT_OPTIONS } from '@angular/material/form-field';

import { routes } from './app.routes';
import { authInterceptor, errorInterceptor } from './core/http.interceptors';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      // Route params arrive as component inputs, so a detail screen needs no ActivatedRoute wiring.
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'top', anchorScrolling: 'enabled' }),
    ),
    provideHttpClient(
      // Order matters: auth runs first so a retried request carries the refreshed token, and the
      // error interceptor sits outside it so a silently-refreshed 401 never raises a toast.
      withInterceptors([errorInterceptor, authInterceptor]),
    ),
    {
      provide: MAT_FORM_FIELD_DEFAULT_OPTIONS,
      // `dynamic` is what removes most of the empty space in this app's forms. By default every
      // Material field reserves a line underneath itself for an error that is usually not there —
      // about 22px per field, so a six-field dialog carried 130px of nothing. Dynamic collapses it
      // and lets the field grow only when there is something to say.
      useValue: { appearance: 'outline', subscriptSizing: 'dynamic' },
    },
  ],
};
