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

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly router = inject(Router);

  private readonly accessTokenState = signal<string | null>(null);

  // Concurrent 401s await this in-flight promise, so only one /refresh call runs.
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
