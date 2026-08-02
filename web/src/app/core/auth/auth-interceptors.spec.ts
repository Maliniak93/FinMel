import type { AuthService } from './auth';
import { configureAuthInterceptors } from './auth-interceptors';

type RequestFn = (request: Request, options: FakeOptions) => Request | Promise<Request>;
type ResponseFn = (
  response: Response,
  request: Request,
  options: FakeOptions,
) => Response | Promise<Response>;

interface FakeOptions {
  url: string;
  method?: string;
  headers: Headers;
  serializedBody?: BodyInit;
  fetch?: typeof fetch;
}

function createFakeClient() {
  const requestFns: RequestFn[] = [];
  const responseFns: ResponseFn[] = [];
  return {
    interceptors: {
      request: { use: (fn: RequestFn) => requestFns.push(fn) },
      response: { use: (fn: ResponseFn) => responseFns.push(fn) },
    },
    requestFns,
    responseFns,
  };
}

function fakeAuthService(overrides: Partial<AuthService> = {}): AuthService {
  return {
    accessToken: vi.fn().mockReturnValue(null),
    refreshOnce: vi.fn().mockResolvedValue(false),
    forceLogout: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  } as unknown as AuthService;
}

describe('configureAuthInterceptors', () => {
  it('attaches the Authorization header when a token is present', () => {
    const authService = fakeAuthService({ accessToken: vi.fn().mockReturnValue('the-token') });
    const client = createFakeClient();
    configureAuthInterceptors(authService, [client]);

    const request = new Request('https://example.test/api/portfolio/portfolios');
    const result = client.requestFns[0](request, {
      url: '/api/portfolio/portfolios',
      headers: new Headers(),
    });

    expect((result as Request).headers.get('Authorization')).toBe('Bearer the-token');
  });

  it('does not set an Authorization header when there is no token', () => {
    const authService = fakeAuthService();
    const client = createFakeClient();
    configureAuthInterceptors(authService, [client]);

    const request = new Request('https://example.test/api/portfolio/portfolios');
    const result = client.requestFns[0](request, {
      url: '/api/portfolio/portfolios',
      headers: new Headers(),
    });

    expect((result as Request).headers.has('Authorization')).toBe(false);
  });

  it('passes non-401 responses through unchanged', async () => {
    const authService = fakeAuthService();
    const client = createFakeClient();
    configureAuthInterceptors(authService, [client]);

    const response = new Response(null, { status: 200 });
    const request = new Request('https://example.test/api/portfolio/portfolios');

    const result = await client.responseFns[0](response, request, {
      url: '/api/portfolio/portfolios',
      headers: new Headers(),
    });

    expect(result).toBe(response);
    expect(authService.refreshOnce).not.toHaveBeenCalled();
  });

  it('does not attempt a refresh for a 401 from the login endpoint itself', async () => {
    const authService = fakeAuthService();
    const client = createFakeClient();
    configureAuthInterceptors(authService, [client]);

    const response = new Response(null, { status: 401 });
    const request = new Request('https://example.test/api/identity/login');

    const result = await client.responseFns[0](response, request, {
      url: '/api/identity/login',
      headers: new Headers(),
    });

    expect(result).toBe(response);
    expect(authService.refreshOnce).not.toHaveBeenCalled();
  });

  it('refreshes once and retries the original request on a 401', async () => {
    const retriedResponse = new Response(null, { status: 200 });
    const fetchFn = vi.fn().mockResolvedValue(retriedResponse);
    const authService = fakeAuthService({
      accessToken: vi.fn().mockReturnValue('fresh-token'),
      refreshOnce: vi.fn().mockResolvedValue(true),
    });
    const client = createFakeClient();
    configureAuthInterceptors(authService, [client]);

    const response = new Response(null, { status: 401 });
    const request = new Request('https://example.test/api/portfolio/portfolios');

    const result = await client.responseFns[0](response, request, {
      url: '/api/portfolio/portfolios',
      method: 'GET',
      headers: new Headers({ Authorization: 'Bearer stale-token' }),
      fetch: fetchFn,
    });

    expect(authService.refreshOnce).toHaveBeenCalledTimes(1);
    expect(fetchFn).toHaveBeenCalledTimes(1);
    const retriedRequest = fetchFn.mock.calls[0][0] as Request;
    expect(retriedRequest.headers.get('Authorization')).toBe('Bearer fresh-token');
    expect(result).toBe(retriedResponse);
  });

  it('forces logout and returns the original 401 when refresh fails', async () => {
    const authService = fakeAuthService({ refreshOnce: vi.fn().mockResolvedValue(false) });
    const client = createFakeClient();
    configureAuthInterceptors(authService, [client]);

    const response = new Response(null, { status: 401 });
    const request = new Request('https://example.test/api/portfolio/portfolios');

    const result = await client.responseFns[0](response, request, {
      url: '/api/portfolio/portfolios',
      headers: new Headers(),
    });

    expect(authService.forceLogout).toHaveBeenCalledTimes(1);
    expect(result).toBe(response);
  });
});
