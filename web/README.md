# web/

Angular workspace lands in T1.8. For now this directory holds the OpenAPI → TypeScript client
generation tooling (T1.7) that T1.8 and later frontend tasks build on.

## Prerequisites

- Local stack running: `dotnet run --project Skarbiec.AppHost` (Postgres, RabbitMQ, all 5 services,
  the Gateway — from the repo root).
- Node 22.18+ (`@hey-api/openapi-ts` requirement).

## Regenerating the API clients

```
npm install
npm run gen:api
```

`gen:api` fetches each service's OpenAPI document through the Gateway
(`/api/<service>/openapi/v1.json`) and writes a typed client to `src/app/api/<service>/`, per
`openapi-ts.config.ts`. Regenerating after any API-changing backend task is part of that task's DoD
(CLAUDE.md Workflow).

Generated code is **committed**, not gitignored — CI and reviewers see client-shape changes as a
normal diff, and `npm run gen:api` doesn't need the stack running just to typecheck/build.

- Target: the Gateway, not services directly (ADR-013). Each service's OpenAPI endpoint is mapped
  under its own `/api/<service>/openapi/...` prefix so it flows through that service's *existing*
  Gateway route with no extra YARP config — see `Skarbiec.ServiceDefaults/OpenApi/OpenApiExtensions.cs`.
- Development only. `MapOpenApi` is only called when `IHostEnvironment.IsDevelopment()`; production
  builds never map the endpoint, so it 404s regardless of Gateway config. The Gateway's own
  `*-openapi` routes use `AuthorizationPolicy: anonymous` (safe, since there's nothing to
  authorize once the endpoint doesn't exist).
- Override the Gateway URL with `SKARBIEC_GATEWAY_URL` (defaults to
  `http://localhost:60684` — the Gateway's fixed local dev HTTP port, see
  `gateway/Skarbiec.Gateway/Properties/launchSettings.json`; plain HTTP avoids the dev-cert trust
  dance for this generation-only tooling).

### Generator: `@hey-api/openapi-ts`

Chosen over `ng-openapi-gen` per T1.7's scope criteria:

- **Maintenance** — active weekly releases vs. `ng-openapi-gen`'s sparse cadence.
- **Signals-friendly output** — generates a plain typed `fetch` client with no Angular/RxJS
  coupling, so it wraps cleanly in `resource()`/`httpResource()` (angular.md's data-access
  convention) instead of fighting an `Observable`-shaped API.

Each service's client (`plugins: client-fetch + typescript + sdk`) ships with **no default
`baseUrl`** (`baseUrl: false`) — .NET's OpenAPI generator stamps a `servers` entry for the
*service's own* dev port (e.g. `https://localhost:60585`), not the Gateway it was fetched through;
inferring from that would silently violate "Angular never calls services directly" (ADR-013).
Every consumer must call `client.setConfig({ baseUrl })` with the Gateway URL explicitly — this
package's `smoke-api.ts` does it for the smoke check; T1.8's environment config will do it for the
real app.

## Verifying a regenerated client

```
npm run typecheck   # tsc --noEmit, strict mode, over the generated clients + scripts/
npm run smoke:api   # register -> login -> list portfolios, end to end through the Gateway
```

`smoke:api` needs the local stack running (same as `gen:api`).
