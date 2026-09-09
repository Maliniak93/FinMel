# Local development

## Prerequisites

- Docker Desktop running — Testcontainers (tests) and Aspire (Postgres/RabbitMQ containers) both need the daemon.
- .NET 10 SDK.
- Node 22.22.3+ / 24.15.0+ / 26+ (Angular CLI 22's floor). Anything below these minors fails obscurely — see `troubleshooting.md`.

## Run the stack

```bash
dotnet run --project Skarbiec.AppHost
```

Starts one PostgreSQL container (a database + a scoped role per service — `identity_db`/`identity_user`, `portfolio_db`/`portfolio_user`, `marketdata_db`/`marketdata_user`, `reporting_db`/`reporting_user`) and one RabbitMQ container (management plugin on), plus every service and the Gateway, wired into the Aspire dashboard (its URL is printed on startup — traces, logs, metrics, resource state, all out of the box). Each service applies its own pending EF Core migrations at startup while `ASPNETCORE_ENVIRONMENT=Development`.

Both Postgres and RabbitMQ use named Docker volumes (`.WithDataVolume()`), so stopping and restarting the AppHost keeps existing local data.

## Frontend dev server

```bash
cd web && npm start
```

`ng serve` on `http://localhost:4200`, proxying `/api/*` to the Gateway's fixed local HTTP port `http://localhost:60684` (`web/proxy.conf.json`) — no CORS setup needed. Requires the backend stack (above) already running.

## Seeds

`MarketDataSeeder` (`services/MarketData/Skarbiec.MarketData/Data/MarketDataSeeder.cs`) seeds the instrument dictionary on startup.

## Resetting local databases

Needed after a destructive or squashed EF migration, or when Postgres/RabbitMQ show "Unhealthy" from a password mismatch (see `troubleshooting.md`):

```bash
docker volume ls                                            # find the postgres/rabbitmq volume names
docker volume rm skarbiec.apphost-<hash>-postgres-data skarbiec.apphost-<hash>-rabbitmq-data
```

Aspire recreates both volumes — and, for Postgres, every per-service database and role — on the next `dotnet run --project Skarbiec.AppHost`. Safe at any time while the project stays local-only: there is no real data to lose.

## Manual sync trigger

The Settings page (`web/src/app/features/settings/`) has a button that triggers MarketData's sync jobs on demand, instead of waiting for the daily schedule.

## `requests/*.http`

`requests/identity.http`, `portfolio.http`, `marketdata.http`, `reporting.http` — manual smoke requests for the [VS Code REST Client](https://marketplace.visualstudio.com/items?itemName=humao.rest-client) extension. `baseUrl` is `https://localhost:60683` (the Gateway's HTTPS dev port); the login request captures `accessToken` into a variable that later requests in the same file reuse. Kept current by whichever spec changes the affected API — part of that spec's Definition of Done.

## Verification

```bash
node scripts/verify.mjs
```

Format check, build, tests (backend, plus `web/`: typecheck, lint, build, test) and an OpenAPI-client diff check. `--quick` runs only format + build of touched projects — the same thing the implementer agent's `Stop` hook runs. This script lands with spec-00; until then use the individual commands from the repo-root `CLAUDE.md` command table.
