# Skarbiec

Personal wealth-management web app and a deliberate microservices learning project.
Stack: .NET 10 (5 services + YARP gateway) · Angular 22 · PostgreSQL (db-per-service) · RabbitMQ + MassTransit v8 (outbox/inbox) · .NET Aspire locally.

## Commands

Run from the repo root (`Skarbiec.sln`).

| Task | Command |
| --- | --- |
| Run everything locally (Aspire: PG + RabbitMQ + services + dashboard) | `dotnet run --project Skarbiec.AppHost` |
| Build | `dotnet build` |
| Test — **requires Docker running** (Testcontainers: PG, RabbitMQ) | `dotnet test` |
| Format (before every commit) | `dotnet format` |
| Frontend dev server | `cd web && npm start` |
| Frontend unit tests (Vitest) | `cd web && npm test` |
| Deploy to VPS | `deploy/deploy.sh <image-tag>` |

If a command's target doesn't exist yet, that part of the scaffold hasn't been built — find the owning task in `skarbiec-plan/zadania/` instead of improvising a substitute.

## Repo layout

```
Skarbiec.AppHost/         # Aspire orchestration — the local F5 entry point
Skarbiec.ServiceDefaults/ # OTel, health checks, JWT auth, HTTP resilience — referenced by every service
services/                 # 5 microservices; vertical slices inside (Features/<Name>/)
gateway/                  # YARP
contracts/                # Skarbiec.Contracts — event/DTO records, additive versioning
web/                      # Angular 22 frontend
deploy/                   # docker compose (VPS); k3s in Phase 5
skarbiec-plan/            # planning docs — local only (gitignored)
```

## Documentation (read on demand — do not load all upfront)

| File | Content |
| --- | --- |
| `skarbiec-plan/01-vision-and-scope.md` | vision, MVP vs v1.0 scope |
| `skarbiec-plan/02-architecture.md` | services, communication, data, stack |
| `skarbiec-plan/03-domain-model.md` | entities per service, valuation algorithm |
| `skarbiec-plan/04-roadmap.md` | phases 0–5, risks |
| `skarbiec-plan/05-backlog.md` | epics E1–E9 with acceptance criteria |
| `skarbiec-plan/06-adr-decisions.md` | 17 ADRs — check before any architectural change |
| `skarbiec-plan/zadania/phase-*.md` | granular tasks per phase (T-numbered, with AC) |

## Hard architecture rules (from ADRs — never violate without a new ADR)

- 5 services + gateway: Identity, Portfolio, MarketData, Strategy, Reporting (+ Notifications in Phase 4). No further splitting without an ADR (ADR-001).
- Vertical slice inside services: feature = folder (endpoint + handler + validator). No Service/Repository layers. No MediatR (ADR-002, ADR-004).
- Handlers return `Result`/`Result<T>` for expected failures — never throw for expected error paths; the endpoint maps a failed `Result` to ProblemDetails (ADR-017).
- Database per service. No cross-DB joins. Cross-service references by ID only, no foreign keys (ADR-003).
- Events published only through MassTransit EF Outbox; consumers idempotent (inbox/dedup by MessageId) (ADR-012).
- Every user-owned entity has `UserId` from JWT claims — never from the request body. EF global query filter per service; tenancy isolation tests are part of DoD (ADR-006).
- Money: `decimal` + `Money` value object; base currency PLN (ADR-008).
- External price APIs (NBP, Stooq, CoinGecko) called only from Quartz jobs in MarketData — never in a request path (ADR-007).
- Transactions are the source of truth for asset quantity (ADR-009).
- Angular talks only to the Gateway (ADR-013).

## Conventions

Full conventions are path-scoped rules in `.claude/rules/`, loaded automatically when touching matching files: `dotnet.md` (`*.cs`/`*.csproj`), `angular.md` (`web/**`), `domain.md` (services/contracts/gateway/web). **When creating brand-new files in an area whose rule hasn't loaded yet, read that rule file first.**

Always-true essentials:

- C# 14 / .NET 10: nullable enabled, file-scoped namespaces, records for DTOs/events/contracts, `sealed` by default; Minimal APIs with `TypedResults`; handlers return `Result`/`Result<T>` for expected failures (never throw), mapped to ProblemDetails at the endpoint (ADR-017).
- Angular 22: standalone components, signals-first, zoneless + OnPush (framework defaults — don't opt out), native control flow; data access only via the OpenAPI-generated TS client through the Gateway.
- Angular 22 and .NET 10 evolve past training data — when unsure about an API, check docs (microsoft-docs MCP for .NET/ASP.NET/EF; context7 for Angular, MassTransit) instead of writing from memory.

## Workflow

- Skills: `/new-slice <Service> <Feature>` · `/new-event <Event> <Publisher> [Consumer]` · `/new-service <Name>` (requires an ADR) · `/new-adr <decision>`.
- Definition of done for a slice: tests green (incl. tenancy isolation), zero warnings (warnings are errors), `dotnet format` clean.

## Response style

- Be terse: no narration of steps taken, no restating file contents, no closing summaries unless asked.
- One short clarifying question beats guessing.
