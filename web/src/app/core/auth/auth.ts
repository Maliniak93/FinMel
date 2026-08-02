import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

import {
  postApiIdentityLogin,
  postApiIdentityLogout,
  postApiIdentityRefresh,
  postApiIdentityRegister,
  type LoginRequest,
  type RegisterRequest,
} from '../../api/identity';
import { readProblemDetails, type ApiProblemDetails } from './problem-details';

export type AuthResult = { success: true } | { success: false; problem: ApiProblemDetails };

// Session state (ADR-005): the access token lives in memory only (this signal), never
// localStorage; the refresh token stays in the httpOnly cookie the Identity service manages —
// this service never reads or writes it directly.
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly router = inject(Router);

  private readonly accessTokenState = signal<string | null>(null);

  // Concurrent 401s (e.g. a page firing several requests at once) must trigger exactly one
  // /refresh call; latecomers await this same in-flight promise instead of racing.
  private refreshInFlight: Promise<boolean> | null = null;

  readonly isAuthenticated = computed(() => this.accessTokenState() !== null);

  accessToken(): string | null {
    return this.accessTokenState();
  }

  async register(request: RegisterRequest): Promise<AuthResult> {
    const result = await postApiIdentityRegister({ body: request });
    if (result.error) {
      return { success: false, problem: readProblemDetails(result.error) };
    }
    return { success: true };
  }

  async login(request: LoginRequest): Promise<AuthResult> {
    const result = await postApiIdentityLogin({ body: request });
    if (result.error || !result.data) {
      return { success: false, problem: readProblemDetails(result.error) };
    }
    this.accessTokenState.set(result.data.accessToken);
    return { success: true };
  }

  async logout(): Promise<void> {
    await postApiIdentityLogout();
    this.accessTokenState.set(null);
    await this.router.navigateByUrl('/login');
  }

  // Called once by the app initializer on load (silent session restore from the refresh cookie)
  // and by the response interceptor on a 401 — both paths share the same single-flight promise.
  async refreshOnce(): Promise<boolean> {
    this.refreshInFlight ??= this.performRefresh();
    try {
      return await this.refreshInFlight;
    } finally {
      this.refreshInFlight = null;
    }
  }

  async forceLogout(): Promise<void> {
    this.accessTokenState.set(null);
    await this.router.navigateByUrl('/login');
  }

  private async performRefresh(): Promise<boolean> {
    const result = await postApiIdentityRefresh();
    if (result.error || !result.data) {
      this.accessTokenState.set(null);
      return false;
    }
    this.accessTokenState.set(result.data.accessToken);
    return true;
  }
}
