import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideNativeDateAdapter } from '@angular/material/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';

import { routes } from './app.routes';
import { apiClients } from './core/api-clients';
import { AuthService } from './core/auth/auth';
import { configureAuthInterceptors } from './core/auth/auth-interceptors';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideNativeDateAdapter(),
    provideAppInitializer(() => {
      const authService = inject(AuthService);
      configureAuthInterceptors(authService, apiClients);
      // Silent session restore from the refresh cookie (T1.9 AC: hard refresh keeps the
      // session) — awaited so the initial route's guards see the correct auth state.
      return authService.refreshOnce();
    }),
  ],
};
