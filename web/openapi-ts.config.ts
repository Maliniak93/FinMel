const services = ['identity', 'portfolio', 'marketdata', 'strategy', 'reporting'] as const;

// The Gateway's fixed local dev HTTP port (gateway/Skarbiec.Gateway/Properties/launchSettings.json).
// Plain HTTP sidesteps the dev-cert trust dance for what's purely local generation tooling (T1.7)
// — the Gateway never redirects HTTP to HTTPS.
const gatewayUrl = process.env['SKARBIEC_GATEWAY_URL'] ?? 'http://localhost:60684';

export default services.map((service) => ({
  input: `${gatewayUrl}/api/${service}/openapi/v1.json`,
  output: `src/app/api/${service}`,
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
