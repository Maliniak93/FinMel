import { client as identityClient } from '../api/identity/client.gen';
import { client as marketDataClient } from '../api/marketdata/client.gen';
import { client as portfolioClient } from '../api/portfolio/client.gen';
import { client as reportingClient } from '../api/reporting/client.gen';
import { environment } from '../../environments/environment';

export const apiClients = [identityClient, marketDataClient, portfolioClient, reportingClient];

export function configureApiClients(): void {
  for (const client of apiClients) {
    client.setConfig({ baseUrl: environment.gatewayUrl });
  }
}
