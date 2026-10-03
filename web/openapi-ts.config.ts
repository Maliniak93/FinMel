const services = ['identity', 'portfolio', 'marketdata', 'reporting'] as const;

export default services.map((service) => ({
  input: `./openapi/${service}.json`,
  output: { path: `src/app/api/${service}`, module: { extension: '.js' } },
  plugins: [
    // The generated `servers` entry is the service's own dev port, never the Gateway, so every caller sets baseUrl itself.
    { name: '@hey-api/client-fetch', baseUrl: false },
    '@hey-api/typescript',
    '@hey-api/sdk',
  ],
}));
