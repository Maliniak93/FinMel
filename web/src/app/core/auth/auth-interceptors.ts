import type { AuthService } from './auth';

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
      use(
        fn: (request: Request, options: ResolvedRequestOptions) => Request | Promise<Request>,
      ): unknown;
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

// A 401 from these is a credentials verdict for the caller; refreshing and retrying would loop or mask it.
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

      // The first fetch consumed the body stream, so the retry rebuilds the body from serializedBody.
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
