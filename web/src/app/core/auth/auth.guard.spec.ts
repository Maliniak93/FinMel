import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AuthService } from './auth';
import { authGuard, publicGuard } from './auth.guard';

function setup(isAuthenticated: boolean) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: AuthService, useValue: { isAuthenticated: () => isAuthenticated } },
    ],
  });
}

describe('authGuard', () => {
  it('allows activation when authenticated', () => {
    setup(true);
    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));
    expect(result).toBe(true);
  });

  it('redirects to /login when not authenticated', () => {
    setup(false);
    const result = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never));
    const router = TestBed.inject(Router);
    expect(result).toEqual(router.createUrlTree(['/login']));
  });
});

describe('publicGuard', () => {
  it('allows activation when not authenticated', () => {
    setup(false);
    const result = TestBed.runInInjectionContext(() => publicGuard({} as never, {} as never));
    expect(result).toBe(true);
  });

  it('redirects to /dashboard when already authenticated', () => {
    setup(true);
    const result = TestBed.runInInjectionContext(() => publicGuard({} as never, {} as never));
    const router = TestBed.inject(Router);
    expect(result).toEqual(router.createUrlTree(['/dashboard']));
  });
});
