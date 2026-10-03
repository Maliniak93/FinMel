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
import { provideI18n } from './core/i18n/i18n';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideNativeDateAdapter(),
    provideI18n(),
    provideAppInitializer(() => {
      const authService = inject(AuthService);
      configureAuthInterceptors(authService, apiClients);
      // Awaited so the initial route's guards see the restored session.
      return authService.refreshOnce();
    }),
  ],
};
