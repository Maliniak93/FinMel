# web/

Angular 22 workspace: standalone components, zoneless + OnPush, Angular Material (ADR-010), routing
through a single app shell. All HTTP goes through the Gateway (ADR-013) via the OpenAPI-generated
TS clients checked into `src/app/api/`.

## Prerequisites

- Local stack running: `dotnet run --project Skarbiec.AppHost` (Postgres, RabbitMQ, all 5 services,
  the Gateway — from the repo root).
- Node 22.22.3+ / 24.15.0+ / 26+ (Angular CLI 22 requirement).

## Development server

```bash
npm install
npm start          # ng serve — http://localhost:4200, proxies /api/* to the Gateway (proxy.conf.json)
```

The dev server proxies `/api/*` requests to the Gateway's fixed local dev HTTP port
(`http://localhost:60684`, see `gateway/Skarbiec.Gateway/Properties/launchSettings.json`) per
`proxy.conf.json`, so the app and its generated API clients can use relative URLs
(`environment.gatewayUrl`) with no CORS setup. `src/app/core/api-clients.ts` configures every
generated client's `baseUrl` from `environment.gatewayUrl` once at bootstrap (`main.ts`).

## Building

```bash
npm run build       # dist/, production optimized
npm run watch        # ng build --watch, development configuration
```

## Tests, lint, format

```bash
npm test             # ng test — Vitest
npm run lint          # ng lint — ESLint (angular-eslint)
npm run format        # prettier --write .
npm run format:check  # prettier --check .
```

## Regenerating the API clients

```bash
npm run gen:api
```

`gen:api` fetches each service's OpenAPI document through the Gateway
(`/api/<service>/openapi/v1.json`) and writes a typed client to `src/app/api/<service>/`, per
`openapi-ts.config.ts`. Regenerating after any API-changing backend task is part of that task's DoD
(CLAUDE.md Workflow).

Generated code is **committed**, not gitignored — CI and reviewers see client-shape changes as a
normal diff, and `npm run gen:api` doesn't need the stack running just to typecheck/build. It's
excluded from `npm run lint` (`eslint.config.js`) since it's not hand-maintained.

- Target: the Gateway, not services directly (ADR-013). Each service's OpenAPI endpoint is mapped
  under its own `/api/<service>/openapi/...` prefix so it flows through that service's _existing_
  Gateway route with no extra YARP config — see `Skarbiec.ServiceDefaults/OpenApi/OpenApiExtensions.cs`.
- Development only. `MapOpenApi` is only called when `IHostEnvironment.IsDevelopment()`; production
  builds never map the endpoint, so it 404s regardless of Gateway config. The Gateway's own
  `*-openapi` routes use `AuthorizationPolicy: anonymous` (safe, since there's nothing to
  authorize once the endpoint doesn't exist).
- Override the Gateway URL used by generation/smoke tooling with `SKARBIEC_GATEWAY_URL` (defaults to
  `http://localhost:60684`; plain HTTP avoids the dev-cert trust dance for this generation-only
  tooling — unrelated to the app's own dev proxy above).

### Generator: `@hey-api/openapi-ts`

Chosen over `ng-openapi-gen` per T1.7's scope criteria:

- **Maintenance** — active weekly releases vs. `ng-openapi-gen`'s sparse cadence.
- **Signals-friendly output** — generates a plain typed `fetch` client with no Angular/RxJS
  coupling, so it wraps cleanly in `resource()`/`httpResource()` (angular.md's data-access
  convention) instead of fighting an `Observable`-shaped API.

Each service's client (`plugins: client-fetch + typescript + sdk`) ships with **no default
`baseUrl`** (`baseUrl: false`) — .NET's OpenAPI generator stamps a `servers` entry for the
_service's own_ dev port (e.g. `https://localhost:60585`), not the Gateway it was fetched through;
inferring from that would silently violate "Angular never calls services directly" (ADR-013).
Every consumer must call `client.setConfig({ baseUrl })` explicitly — `scripts/smoke-api.ts` does
it for the smoke check; `src/app/core/api-clients.ts` does it for the real app at bootstrap.

## Verifying a regenerated client

```bash
npm run typecheck   # tsc --noEmit, strict mode, over src/app/api/ + scripts/ (tsconfig.tools.json)
npm run smoke:api   # register -> login -> list portfolios, end to end through the Gateway
```

`smoke:api` needs the local stack running (same as `gen:api`).
