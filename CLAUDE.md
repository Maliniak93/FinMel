# Skarbiec

Personal wealth-management web app and a deliberate microservices learning project.
Stack: .NET 10 (4 services + YARP gateway) · Angular 22 · PostgreSQL (db per service) · RabbitMQ + MassTransit v8 (outbox/inbox) · .NET Aspire locally.

## Project state: greenfield (ADR-019)
One user, no real data, local-only — so: edit contracts in place (never add a `V2` type); write destructive migrations with no backfill (squashing a service to one `InitialCreate` is allowed); delete replaced code, tests and docs instead of deprecating them; propose a rewrite when it beats a patch.
Greenfield changes none of: tenancy isolation, outbox + idempotent consumers, the Result pattern, the definition of done.

## Commands
| Task | Command |
| --- | --- |
| Run everything locally (Aspire: Postgres + RabbitMQ + services + dashboard) | `dotnet run --project Skarbiec.AppHost` |
| Build | `dotnet build Skarbiec.slnx` |
| Test — **Docker must be running** (Testcontainers) | `dotnet test` |
| Format check | `dotnet format Skarbiec.slnx --verify-no-changes` |
| One-shot verification: format → build → tests → web → API client | `node scripts/verify.mjs [--quick\|--all\|--projects A,B]` |
| Frontend dev server | `cd web && npm start` |
| Frontend unit tests (Vitest) | `cd web && npm test` |
| Regenerate the TS client after an API change — reads the build-time OpenAPI files once spec-00 lands, until then needs the stack running | `cd web && npm run gen:api` |
| Add a migration, then always `dotnet format` | `dotnet ef migrations add <Name> --project services/<S>/Skarbiec.<S>` |

## Repo layout
```
Skarbiec.AppHost/         # Aspire orchestration — local entry point
Skarbiec.ServiceDefaults/ # OTel, health, JWT, resilience, Result→ProblemDetails, messaging, tenancy
Skarbiec.Testing/         # containers fixture, api factory, tenancy template
services/                 # Identity, Portfolio, MarketData, Reporting (Strategy removed by spec-01) — slices in Features/<Name>/
gateway/                  # YARP
contracts/                # Skarbiec.Contracts — events/DTOs edited in place, Result, Money, enums
web/                      # Angular 22 frontend
scripts/  requests/       # verify.mjs · .http files per service
skarbiec-plan/            # planning docs: product, architecture, domain, decisions, workflow, ideas, specs/, runbooks/ (archive/ = history)
```

## Hard architecture rules
1. 4 services + gateway: Identity, Portfolio, MarketData, Reporting. No new service without an ADR (ADR-001, ADR-020).
2. Vertical slices: a feature is a folder with endpoint + handler + validator. No Service/Repository layers, no MediatR (ADR-002, ADR-004).
3. Handlers return `Result`/`Result<T>`; the endpoint maps a failure to ProblemDetails. Never throw for an expected failure (ADR-017).
4. Database per service. Reference other services by id only — no FKs, no cross-DB queries (ADR-003).
5. Domain facts are events carrying full state, published only through the MassTransit EF outbox; consumers are idempotent (inbox) and never call the publisher back (ADR-012, ADR-021).
6. REST between services only for request-path validation (Portfolio→MarketData instrument lookup, JWT forwarded) and the daily Reporting→MarketData price/FX batch (`SystemCaller`). No gRPC (ADR-021, ADR-022).
7. External price APIs are called only from MarketData jobs; the single exception is ticker verification through `ITickerVerifier` (ADR-007, ADR-018).
8. Every user-owned entity carries `UserId` from JWT claims — never from the request. EF global query filter; tenancy isolation tests are part of DoD (ADR-006).
9. Money is `decimal`/`Money`, base currency PLN (ADR-008).
10. Transactions are the source of truth for asset quantity (ADR-009).
11. Angular talks only to the Gateway, through the generated client (ADR-013).

## Conventions
Path-scoped rules in `.claude/rules/` (`dotnet.md`, `messaging.md`, `testing.md`, `angular.md`, `domain.md`) load automatically when you touch matching files — read the matching rule before creating files in an area you have not touched yet. .NET 10 and Angular 22 move faster than training data: verify an API through microsoft-docs (.NET/ASP.NET/EF) or context7 (Angular, MassTransit) instead of writing it from memory.

## Workflow
`/design <idea>` writes a spec into `skarbiec-plan/specs/` and stops for your approval → `/build <spec>` runs test-writer → implementer → verifier → reviewer → ops, which opens a PR on `feat/<slug>` → you merge. `/fix <bug>` reproduces, writes a one-criterion fix spec and runs the same pipeline; `/check` runs verification; `/ops <task>` handles CI and GitHub chores.
Tier 1 = a precedent for this exists in the same service (Sonnet). Tier 2 = new pattern, cross-service work, or an algorithm (Opus).
Definition of done: tests green including tenancy, zero warnings, `dotnet format` clean, TS client regenerated when the API changed, `requests/*.http` updated, rules and ADRs updated when a convention changes.
Agents never run git — only `ops`, and only on `feat/*`.

## Documentation (read on demand)
| File | Content |
| --- | --- |
| `skarbiec-plan/architecture.md` | services, communication, flows |
| `skarbiec-plan/domain.md` | data model per service, valuation |
| `skarbiec-plan/decisions.md` | ADRs — check before any architectural change |
| `skarbiec-plan/workflow.md` · `ideas.md` · `runbooks/` | agents, DoR/DoD, git conventions · feature pool · local-dev, ci, troubleshooting |
| in repo | `Skarbiec.Testing/README.md`, `Skarbiec.ServiceDefaults/Messaging/README.md`, `web/README.md`, `deploy/README.md`, `contracts/Skarbiec.Contracts/CONTRACTS.md` |

## Gotchas
- `dotnet test` needs Docker running; test hosts set `Testing:DisableBackgroundJobs`, so Quartz jobs are off and triggers are NoOp.
- After a destructive migration, drop the local databases — `skarbiec-plan/runbooks/local-dev.md`.
- The Gateway rate limit is 100 requests / 10 s; seed and smoke scripts trip it.
- The Angular CLI needs Node ≥ 22.22.3 / 24.15 / 26.
- `npm run gen:api` occasionally drops `.js` import extensions across the whole client — diff it and keep only the real schema delta.

## Response style
Be terse: no narration of steps, no restating file contents, no closing summaries unless asked. One short clarifying question beats guessing. Every file is written in English; talk to the user in Polish.
