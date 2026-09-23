# Skarbiec

Personal wealth-management web app and a deliberate microservices learning project — .NET 10
(4 services + a YARP gateway), Angular 22, PostgreSQL (database per service), RabbitMQ + MassTransit v8,
orchestrated locally with .NET Aspire.

See `skarbiec-plan/` for vision, architecture, domain model, roadmap, backlog and ADRs;
see `CLAUDE.md` for conventions and the agent workflow.

---

## Prerequisites

| Requirement | Notes |
| --- | --- |
| **Docker Desktop, running** | Aspire starts Postgres + RabbitMQ containers; the test suite uses Testcontainers. Nothing works without it. |
| **.NET 10 SDK** | `dotnet --version` |
| **Node 22.22.3+ / 24.15.0+ / 26+** | Angular CLI 22's floor — lower minors fail with obscure errors (`skarbiec-plan/runbooks/troubleshooting.md`). |

First checkout:

```bash
dotnet restore Skarbiec.slnx
cd web && npm install && cd ..
```

---

## Run the whole app locally

Two terminals — backend first, then the frontend.

**1. Backend (Aspire orchestrates everything):**

```bash
dotnet run --project Skarbiec.AppHost
```

Starts one PostgreSQL container (a database + a scoped role per service), one RabbitMQ container
(management plugin on), all four services, the Gateway and the Aspire dashboard. Each service applies
its own pending EF Core migrations at startup in `Development`. Both containers use named Docker
volumes, so data survives a restart.

The **Aspire dashboard URL is printed on startup** (`https://localhost:17077`, with a login token in
the console) — logs, traces, metrics and resource health for every service in one place.

**2. Frontend:**

```bash
cd web && npm start
```

`ng serve` on <http://localhost:4200>, proxying `/api/*` to the Gateway (`web/proxy.conf.json`) —
no CORS setup. Needs the backend running.

### Local addresses

| What | URL |
| --- | --- |
| Angular app | http://localhost:4200 |
| Aspire dashboard | https://localhost:17077 (token printed at startup) |
| Gateway | https://localhost:60683 · http://localhost:60684 |
| Identity | https://localhost:60583 · http://localhost:60584 |
| Portfolio | https://localhost:60585 · http://localhost:60586 |
| MarketData | https://localhost:60587 · http://localhost:60588 |
| Reporting | https://localhost:60591 · http://localhost:60592 |
| RabbitMQ management | port shown in the Aspire dashboard |

Angular talks **only** to the Gateway (ADR-013); the per-service ports are for debugging and
`requests/*.http`.

---

## Command reference

### Build, test, verify

```bash
dotnet build Skarbiec.slnx                 # build everything
dotnet test                                # all tests — Docker must be running (Testcontainers)
dotnet test services/Portfolio/Skarbiec.Portfolio.Tests   # one project
dotnet format Skarbiec.slnx --verify-no-changes           # format check
dotnet format Skarbiec.slnx                               # format in place

node scripts/verify.mjs                    # the single definition of "green"
node scripts/verify.mjs --quick            # format + build of touched projects only
node scripts/verify.mjs --all              # every test project + web + api checks
node scripts/verify.mjs --projects Portfolio,Gateway      # exactly these test projects
node scripts/verify.mjs --web --api        # force the web / generated-client checks on
```

With no flags `verify.mjs` picks the affected projects from the diff against `master`. It always ends
with a `VERIFY_RESULT: <json>` line and exits 2 on failure.

### Frontend (`cd web`)

```bash
npm start            # ng serve — http://localhost:4200
npm run build        # production build into dist/
npm run watch        # development build, watching
npm test             # Vitest (ng test)
npm run lint         # ESLint (angular-eslint)
npm run format       # prettier --write .
npm run format:check # prettier --check .
npm run typecheck    # tsc --noEmit over src/app/api + scripts
npm run smoke:api    # register -> login -> list portfolios through the Gateway (needs the stack running)
```

### Regenerating the TypeScript API client

Required whenever a backend API changes (part of the definition of done):

```bash
dotnet build Skarbiec.slnx   # writes web/openapi/<service>.json at build time — no running stack needed
cd web && npm run gen:api
npm run typecheck            # gen:api occasionally drops .js import extensions — diff and keep only the real delta
```

### EF Core migrations

```bash
dotnet ef migrations add <Name> --project services/Portfolio/Skarbiec.Portfolio
dotnet ef migrations remove   --project services/Portfolio/Skarbiec.Portfolio
dotnet format Skarbiec.slnx   # always, right after adding a migration
```

Migrations are applied automatically at service startup in `Development`.

### Manual API calls

`requests/identity.http`, `portfolio.http`, `marketdata.http`, `reporting.http` — for the VS Code
REST Client extension. `baseUrl` is the Gateway's HTTPS dev port (`https://localhost:60683`); the
login request captures `accessToken` for the requests below it in the same file.

### Planning

```bash
node scripts/plan-status.mjs            # spec status vs. git and open PRs
node scripts/plan-status.mjs --write    # refresh the block in skarbiec-plan/README.md
node scripts/plan-status.mjs --no-gh    # skip the GitHub calls
```

---

## Resetting local state

After a destructive or squashed migration, or when Postgres/RabbitMQ show "Unhealthy":

```bash
docker volume ls                                       # find the volume names
docker volume rm skarbiec.apphost-<hash>-postgres-data skarbiec.apphost-<hash>-rabbitmq-data
```

Aspire recreates both volumes — and every per-service database and role — on the next AppHost run.
Safe at any time: the project is local-only, there is no real data to lose.

---

## Gotchas

- `dotnet test` **requires Docker**; test hosts set `Testing:DisableBackgroundJobs`, so Quartz jobs are off.
- The Gateway rate-limits to **100 requests / 10 s** — seed and smoke scripts can trip it.
- MarketData seeds the instrument dictionary at startup; the Settings page has a button that triggers
  its sync jobs on demand instead of waiting for the daily schedule.
- More symptoms and fixes: `skarbiec-plan/runbooks/troubleshooting.md`; deeper setup notes:
  `skarbiec-plan/runbooks/local-dev.md`.
