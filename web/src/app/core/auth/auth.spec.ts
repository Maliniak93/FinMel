import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { client as identityClient } from '../../api/identity/client.gen';
import { AuthService } from './auth';

// The Angular unit-test system blocks `vi.mock` for relative imports ("use TestBed for mocking
// dependencies" instead) — AuthService itself isn't a DI *consumer* of the generated SDK though,
// it calls its plain functions directly, so there's nothing to substitute via TestBed providers.
// Stubbing the underlying `fetch` (what the hey-api client ultimately calls) sidesteps both
// problems: no relative-import mock, and it verifies the single-flight behavior at the network
// boundary instead of trusting a mock to have been wired up correctly.
function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function urlOf(request: Request | string | URL): string {
  return request instanceof Request ? request.url : String(request);
}

describe('AuthService', () => {
  let service: AuthService;
  let router: Router;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  // Unlike the real app (configureApiClients() sets this from the Gateway URL at bootstrap),
  // nothing gives the identity client a baseUrl in a unit test — without one, the relative
  // `/api/identity/...` path fails Node's (non-browser) `Request` constructor, which — unlike a
  // browser's — has no document location to resolve a relative URL against.
  beforeAll(() => {
    identityClient.setConfig({ baseUrl: 'https://example.test' });
  });

  beforeEach(() => {
    fetchSpy = vi.spyOn(globalThis, 'fetch');
    TestBed.configureTestingModule({
      providers: [provideRouter([])],
    });
    service = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  it('starts unauthenticated', () => {
    expect(service.isAuthenticated()).toBe(false);
    expect(service.accessToken()).toBeNull();
  });

  it('becomes authenticated after a successful login', async () => {
    fetchSpy.mockResolvedValue(
      jsonResponse({ accessToken: 'token-1', expiresAtUtc: '2026-01-01T00:15:00Z' }),
    );

    const result = await service.login({ email: 'a@b.com', password: 'secret' });

    expect(result.success).toBe(true);
    expect(service.isAuthenticated()).toBe(true);
    expect(service.accessToken()).toBe('token-1');
  });

  it('stays unauthenticated and surfaces the problem on a failed login', async () => {
    fetchSpy.mockResolvedValue(
      jsonResponse(
        { detail: 'Invalid email or password.', errorCode: 'Unauthorized.InvalidCredentials' },
        401,
      ),
    );

    const result = await service.login({ email: 'a@b.com', password: 'wrong' });

    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.problem.detail).toBe('Invalid email or password.');
    }
    expect(service.isAuthenticated()).toBe(false);
  });

  it('clears the session and redirects to /login on logout', async () => {
    fetchSpy.mockImplementation(async (input: Parameters<typeof fetch>[0]) => {
      const url = urlOf(input);
      if (url.includes('/api/identity/login')) {
        return jsonResponse({ accessToken: 'token-1', expiresAtUtc: '2026-01-01T00:15:00Z' });
      }
      return new Response(null, { status: 204 });
    });

    await service.login({ email: 'a@b.com', password: 'secret' });
    await service.logout();

    expect(service.isAuthenticated()).toBe(false);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('coalesces concurrent refreshes into a single request (single-flight)', async () => {
    // beforeRequest()/request() are themselves async, so `fetch` isn't invoked synchronously —
    // wait for the actual call (however many microtask hops that takes) before resolving it.
    let resolveFetch!: (response: Response) => void;
    const fetchCalled = new Promise<void>((resolveCalled) => {
      fetchSpy.mockImplementation(() => {
        const response = new Promise<Response>((resolve) => {
          resolveFetch = resolve;
        });
        resolveCalled();
        return response;
      });
    });

    const first = service.refreshOnce();
    const second = service.refreshOnce();

    await fetchCalled;
    resolveFetch(jsonResponse({ accessToken: 'token-2', expiresAtUtc: '2026-01-01T00:30:00Z' }));

    const [firstResult, secondResult] = await Promise.all([first, second]);

    expect(firstResult).toBe(true);
    expect(secondResult).toBe(true);
    expect(fetchSpy).toHaveBeenCalledTimes(1);
    expect(service.accessToken()).toBe('token-2');
  });

  it('clears the session when refresh fails', async () => {
    fetchSpy.mockResolvedValue(jsonResponse({ detail: 'invalid refresh token' }, 401));

    const result = await service.refreshOnce();

    expect(result).toBe(false);
    expect(service.isAuthenticated()).toBe(false);
  });
});
