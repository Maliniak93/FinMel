// Only these four services wire build-time OpenAPI generation (below), per ADR-020.
const services = ['identity', 'portfolio', 'marketdata', 'reporting'] as const;

export default services.map((service) => ({
  // Build-time document (spec-00-hygiene): each service's .csproj writes
  // web/openapi/<service>.json on `dotnet build`, via Microsoft.Extensions.ApiDescription.Server —
  // no Gateway/Aspire stack needed to regenerate the client. SKARBIEC_GATEWAY_URL stays
  // (`smoke:api` only, unrelated to generation now).
  input: `./openapi/${service}.json`,
  output: { path: `src/app/api/${service}`, module: { extension: '.js' } },
  plugins: [
    // baseUrl: false — .NET's OpenAPI generator emits a `servers` entry for the *service's own*
    // dev address (e.g. https://localhost:60585), not the Gateway it was fetched through; inferring
    // from that would violate "Angular never calls services directly" (ADR-013). Every consumer
    // (this package's smoke script now, the Angular app's environment config in T1.8) must call
    // `client.setConfig({ baseUrl })` with the Gateway URL explicitly.
    { name: '@hey-api/client-fetch', baseUrl: false },
    '@hey-api/typescript',
    '@hey-api/sdk',
  ],
}));
