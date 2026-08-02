import type { AuthService } from './auth';

// The generated SDKs (T1.7) are @hey-api/client-fetch clients — plain `fetch` under the hood, not
// Angular's HttpClient — so token attachment and 401-refresh-retry hook into *their* interceptor
// API instead of an Angular HttpInterceptorFn. Typed narrowly to what's actually used, so the same
// function wires up every service's client (all generated from the same template).
interface ResolvedRequestOptions {
  url: string;
  method?: string;
  headers: Headers;
  serializedBody?: BodyInit;
  fetch?: typeof fetch;
}

interface InterceptableClient {
  interceptors: {
    request: {
      use(fn: (request: Request, options: ResolvedRequestOptions) => Request | Promise<Request>): unknown;
    };
    response: {
      use(
        fn: (
          response: Response,
          request: Request,
          options: ResolvedRequestOptions,
        ) => Response | Promise<Response>,
      ): unknown;
    };
  };
}

// A 401 from these three is a credentials/session verdict the caller needs to see directly
// (wrong password, expired/reused refresh token) — refreshing-and-retrying it would either loop
// or mask the real error.
const REFRESH_EXEMPT_URLS = new Set([
  '/api/identity/login',
  '/api/identity/register',
  '/api/identity/refresh',
]);

export function configureAuthInterceptors(
  authService: AuthService,
  clients: readonly InterceptableClient[],
): void {
  for (const client of clients) {
    client.interceptors.request.use((request) => {
      const token = authService.accessToken();
      if (token) {
        request.headers.set('Authorization', `Bearer ${token}`);
      }
      return request;
    });

    client.interceptors.response.use(async (response, request, options) => {
      if (response.status !== 401 || REFRESH_EXEMPT_URLS.has(options.url)) {
        return response;
      }

      const refreshed = await authService.refreshOnce();
      if (!refreshed) {
        await authService.forceLogout();
        return response;
      }

      const retryHeaders = new Headers(options.headers);
      retryHeaders.set('Authorization', `Bearer ${authService.accessToken()}`);

      // request.url/method are always readable, but its body stream is already disturbed by the
      // first fetch — rebuild the body from `options.serializedBody`, captured before that fetch.
      const retryRequest = new Request(request.url, {
        method: options.method ?? request.method,
        headers: retryHeaders,
        body: options.serializedBody,
        redirect: 'follow',
      });

      const fetchFn = options.fetch ?? fetch;
      return fetchFn(retryRequest);
    });
  }
}
