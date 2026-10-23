# Local development

## Prerequisites

- Docker Desktop running — Testcontainers (tests) and Aspire (Postgres/RabbitMQ containers) both need the daemon.
- .NET 10 SDK.
- Node 22.22.3+ / 24.15.0+ / 26+ (Angular CLI 22's floor). Anything below these minors fails obscurely — see `troubleshooting.md`.

## Code intelligence for Claude Code (recommended)

Language-server plugins give the build agents (implementer, test-writer, reviewer — they list the
`LSP` tool) go-to-definition / find-references instead of grep-and-read chains, and report type
errors right after an edit instead of at the next build. Set up once per machine, by hand:

1. Language servers, on the `PATH` of the shell you start `claude` from:
   ```
   dotnet tool install -g csharp-ls
   npm install -g typescript-language-server typescript
   ```
   Check: `csharp-ls --version`, `typescript-language-server --version`.
2. In a Claude Code session:
   ```
   /plugin install csharp-lsp@claude-plugins-official
   /plugin install typescript-lsp@claude-plugins-official
   ```
   then `/reload-plugins` (or a new session).
3. Confirm: ask Claude to introduce a type error in a `.cs` file and fix it — a
   `Found N new diagnostic issues` line under the edit means the server runs. Nothing there →
   `/plugin` → **Errors** tab (`Executable not found in $PATH` names the missing binary).

Without the plugins everything still works; the agents fall back to `Grep` + ranged `Read`.

## Run the stack

```bash
dotnet run --project Skarbiec.AppHost
```

Starts one PostgreSQL container (a database + a scoped role per service — `identity_db`/`identity_user`, `portfolio_db`/`portfolio_user`, `marketdata_db`/`marketdata_user`, `reporting_db`/`reporting_user`) and one RabbitMQ container (management plugin on), plus every service and the Gateway, wired into the Aspire dashboard (its URL is printed on startup — traces, logs, metrics, resource state, all out of the box). Each service applies its own pending EF Core migrations at startup while `ASPNETCORE_ENVIRONMENT=Development`.

Both Postgres and RabbitMQ use named Docker volumes (`.WithDataVolume()`), so stopping and restarting the AppHost keeps existing local data.

To try a shipped build without the working tree, the container stack runs from locally built images: `node scripts/compose.mjs build`, then `up` — see `deploy/README.md` "Local: docker compose". `stop-stack.mjs` and `/build` never touch it.

## The e2e stack

Browser tests run on a second compose project, `skarbiec-e2e`, next to the one above and independent of it: `node scripts/e2e.mjs run` builds the images from the working tree (`skarbiec-e2e/<svc>`), starts the stack, runs Playwright from `e2e/` and removes the stack and its data. Its ports: web `http://localhost:18080`, market-data stub `http://localhost:18081/__requests`, Aspire dashboard `http://localhost:18898`; Postgres and RabbitMQ are not published and live on tmpfs. `run --headed` or `run --ui` shows the browser; `run --keep` or `e2e.mjs up` leaves the stack up until `e2e.mjs down`. MarketData reads fixed prices from the stub, never from Yahoo, NBP or the MF page. See `e2e/README.md`.

## Frontend dev server

```bash
cd web && npm start
```

`ng serve` on `http://localhost:4200`, proxying `/api/*` to the Gateway's fixed local HTTP port `http://localhost:60684` (`web/proxy.conf.json`) — no CORS setup needed. Requires the backend stack (above) already running.

## Seeds

`MarketDataSeeder` (`services/MarketData/Skarbiec.MarketData/Data/MarketDataSeeder.cs`) seeds the instrument dictionary on startup.

## Resetting local databases

Needed after a destructive EF migration of an Aspire database, or when Postgres/RabbitMQ show "Unhealthy" from a password mismatch (see `troubleshooting.md`):

```bash
docker volume ls                                            # find the postgres/rabbitmq volume names
docker volume rm skarbiec.apphost-<hash>-postgres-data skarbiec.apphost-<hash>-rabbitmq-data
```

Aspire recreates both volumes — and, for Postgres, every per-service database and role — on the next `dotnet run --project Skarbiec.AppHost`. Safe at any time: Aspire databases hold no data worth keeping. The compose stack's databases are not disposable (ADR-043); `scripts/compose.mjs` backs them up before `up` and `reset`.

A destructive migration of an Aspire database is the same case: drop the volumes above before the next Aspire run so every service migrates cleanly against a fresh, empty database.

A `marketdata_db` still holding Quartz 3.x `quartz.qrtz_*` tables is dropped the same way: Quartz 4 refuses them and creates its own schema at scheduler start.

## Manual sync trigger

The Settings page (`web/src/app/features/settings/`) has a button that triggers MarketData's sync jobs on demand, instead of waiting for the daily schedule.

## `requests/*.http`

`requests/identity.http`, `portfolio.http`, `marketdata.http`, `reporting.http` — manual smoke requests for the [VS Code REST Client](https://marketplace.visualstudio.com/items?itemName=humao.rest-client) extension. `baseUrl` is `https://localhost:60683` (the Gateway's HTTPS dev port); the login request captures `accessToken` into a variable that later requests in the same file reuse. Kept current by whichever spec changes the affected API — part of that spec's Definition of Done.

## Verification

```bash
node scripts/verify.mjs
```

Format check, build, tests (backend, plus `web/`: typecheck, lint, build, test) and an OpenAPI-client diff check. `--quick` runs only format + build of touched projects — the same thing the implementer agent's `Stop` hook runs. This script lands with spec-00; until then use the individual commands from the repo-root `CLAUDE.md` command table.
