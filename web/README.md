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
dotnet build ../Skarbiec.slnx   # writes web/openapi/<service>.json, no stack needed
npm run gen:api
```

`gen:api` no longer needs the stack running: each of Identity/Portfolio/MarketData/Reporting's
`.csproj` writes its OpenAPI document to `web/openapi/<service>.json` at `dotnet build` time
(`Microsoft.Extensions.ApiDescription.Server`, `OpenApiBuildTime`-gated so the build-time process
never touches a live DB/broker/HTTP dependency), and `openapi-ts.config.ts` reads those files
directly and writes a typed client to `src/app/api/<service>/`. Regenerating after any API-changing
backend task is part of that task's DoD (CLAUDE.md Workflow) — just re-run `dotnet build` first so
the `.json` files are current. Strategy is excluded (removed outright by spec-01, never gets a
build-time document).

Generated code is **committed**, not gitignored — CI and reviewers see client-shape changes as a
normal diff, and `npm run gen:api` doesn't need the stack running just to typecheck/build. It's
excluded from `npm run lint` (`eslint.config.js`) since it's not hand-maintained.

- Each service's OpenAPI document is also still mapped at runtime under its own
  `/api/<service>/openapi/...` prefix (development only) so it flows through the Gateway with no
  extra YARP config (ADR-013) — see `Skarbiec.ServiceDefaults/OpenApi/OpenApiExtensions.cs`. That
  runtime endpoint is unaffected by the build-time generation above; nothing reads it for `gen:api`
  anymore.
- `SKARBIEC_GATEWAY_URL` (defaults to `http://localhost:60684`) is now only for `npm run smoke:api`,
  which still exercises the real, running stack through the Gateway.

### Generator: `@hey-api/openapi-ts`

Chosen over `ng-openapi-gen` per T1.7's scope criteria:

- **Maintenance** — active weekly releases vs. `ng-openapi-gen`'s sparse cadence.
- **Signals-friendly output** — generates a plain typed `fetch` client with no Angular/RxJS
  coupling, so it wraps cleanly in `resource()`/`httpResource()` (angular.md's data-access
  convention) instead of fighting an `Observable`-shaped API.

Each service's client (`plugins: client-fetch + typescript + sdk`) ships with **no default
`baseUrl`** (`baseUrl: false`) — never infer one from the OpenAPI document, which would silently
violate "Angular never calls services directly" (ADR-013). Every consumer must call
`client.setConfig({ baseUrl })` explicitly — `scripts/smoke-api.ts` does it for the smoke check;
`src/app/core/api-clients.ts` does it for the real app at bootstrap.

## Verifying a regenerated client

```bash
npm run typecheck   # tsc --noEmit, strict mode, over src/app/api/ + scripts/ (tsconfig.tools.json)
npm run smoke:api   # register -> login -> list portfolios, end to end through the Gateway
```

`smoke:api` needs the local stack running; `gen:api` (above) does not.
