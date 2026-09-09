# Skarbiec — planning documentation

Personal wealth-management application. **Microservices architecture** (.NET 10 + Angular 22 + PostgreSQL + RabbitMQ) — microservices chosen deliberately as a learning goal (ADR-001).

## Document index

| File | Content |
|---|---|
| `01-vision-and-scope.md` | vision, features, MVP vs v1.0, out of scope |
| `02-architecture.md` | services, communication (REST + events), data, Aspire, observability |
| `03-domain-model.md` | entities per service, data ownership, valuation algorithm |
| `04-roadmap.md` | phases 0–5, gantt (~6 months to v1.0), risks |
| `05-backlog.md` | epics E1–E9 (E9 = microservices platform) |
| `06-adr-decisions.md` | 17 architecture decision records |
| `zadania/phase-*.md` | granular task breakdown per phase (format in `zadania/README.md`) |
| `diagrams/c4-context.mermaid` | the system in context |
| `diagrams/services.mermaid` | services, gateway, broker, databases |
| `diagrams/erd.mermaid` | data model with per-service ownership |
| `diagrams/price-sync-sequence.mermaid` | price sync → event → snapshots (outbox/inbox) |

## Architecture in a nutshell

5 services + gateway, monorepo, vertical slices inside services:
**Gateway (YARP)** → **Identity** / **Portfolio** / **MarketData** / **Strategy** / **Reporting** (+ Notifications in Phase 4).
Synchronous REST (+ gRPC as an exercise), asynchronous RabbitMQ + MassTransit v8 with **an outbox and idempotent consumers**. Database per service (single PG instance). Locally .NET Aspire, in production docker compose on an 8 GB VPS, optionally k3s in Phase 5.

## Key guidelines

1. **Few services, many patterns** — the learning lives in the outbox, CQRS, tracing and contracts, not in the number of services. No further splitting without an ADR.
2. **Phase 0 with a hard 5-week limit** — the platform can eat the project; gaps go to the backlog.
3. **Observability from day 1** (OpenTelemetry + Aspire dashboard) — distributed systems are debugged with traces, not with logs from 7 consoles.
4. **Prices from jobs into our own database** (NBP, Stooq, CoinGecko), never live in the request path (ADR-007).
5. **Transactions as the source of truth**, tenancy by `UserId` in every service, `decimal` + PLN as the base currency.

## Open decisions (Phase 0)
- ADR-010: Angular Material vs PrimeNG (spike).
- Application name :)
