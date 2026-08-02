import { client as identityClient } from '../api/identity/client.gen';
import { client as marketDataClient } from '../api/marketdata/client.gen';
import { client as portfolioClient } from '../api/portfolio/client.gen';
import { client as reportingClient } from '../api/reporting/client.gen';
import { client as strategyClient } from '../api/strategy/client.gen';
import { environment } from '../../environments/environment';

// Shared by configureApiClients() below (bootstrap, no DI) and configureAuthInterceptors()
// (app initializer, needs AuthService from DI) — one list of clients for both.
export const apiClients = [
  identityClient,
  marketDataClient,
  portfolioClient,
  reportingClient,
  strategyClient,
];

// ADR-013: every generated client's `baseUrl` is unset (`baseUrl: false` in openapi-ts.config.ts)
// so it can't silently point at a service's own dev port instead of the Gateway. Called once at
// bootstrap (main.ts) so every feature can import a client's `sdk.gen.ts` functions directly.
export function configureApiClients(): void {
  for (const client of apiClients) {
    client.setConfig({ baseUrl: environment.gatewayUrl });
  }
}
