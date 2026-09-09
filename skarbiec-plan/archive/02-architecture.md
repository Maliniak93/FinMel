# 02 — Architecture (microservices)

The architecture has a dual goal: a working application **and learning microservices**. This changes the criteria — we pick patterns worth learning (messaging, outbox, CQRS, distributed tracing) but keep the number of services small so a single person can ship the project.

Rules that keep the project from drowning:

1. **Few services, many patterns** — 5 services + gateway is enough to learn everything that matters. We don't split further "because microservices".
2. **Monorepo** — one git repo, shared builds, atomic contract changes. Multi-repo is pain without gain for 1 person.
3. **Vertical slice inside each service** — feature = folder (endpoint + handler + validator), no layers.
4. **Every phase ends with a deployment** — even if it's compose on a VPS.

## Services

| Service | Responsibility | Database (own!) | Published events |
|---|---|---|---|
| **Gateway** (YARP) | routing, JWT validation, rate limiting, CORS | — | — |
| **Identity** | registration, login, JWT + refresh, profile | `identity_db` | `UserRegistered` |
| **Portfolio** | portfolios, assets, transactions | `portfolio_db` | `AssetChanged`, `TransactionRecorded` |
| **MarketData** | instruments, quotes, FX rates, sync jobs | `marketdata_db` | `DailyPricesSynced` |
| **Strategy** | target allocation, rebalancing, emergency fund, goals | `strategy_db` | — |
| **Reporting** | valuation snapshots, history, exports (CQRS read model) | `reporting_db` | — |
| *(Phase 4)* Notifications | e-mails from events | `notifications_db` | — |

Angular talks **only to the Gateway** (`/api/identity/...`, `/api/portfolio/...`).

## Communication

### Synchronous ("here and now" queries)
- Internal REST between services (HttpClient + `Microsoft.Extensions.Http.Resilience`: retry with jitter, circuit breaker, timeout).
- User context: the user's JWT forwarded in a header (token passthrough) — each service filters by `UserId` from claims itself.
- gRPC: deliberately **later** (Phase 3+) on a single route (Strategy→Portfolio) as an exercise — not everywhere at once.

### Asynchronous (facts that already happened)
- **RabbitMQ** as the broker. Library: **MassTransit v8** (OSS; v9 is commercial — we deliberately stay on v8) or **Rebus** as a lighter alternative. Decision: ADR-012.
- **Outbox pattern is mandatory** (MassTransit EF Outbox): the event is stored in the same transaction as the data and published after commit. This is one of the most important things to learn in this project.
- **Idempotent consumers**: `inbox` table / deduplication by `MessageId` — events may arrive again.
- Event contracts: `Skarbiec.Contracts` project in the monorepo (C# records). Records are edited in place — no `V2` types while ADR-019 (greenfield mode) holds; the only surviving wire rule is that consumers tolerate unknown fields.

### Who talks to whom (example flows)
- **Dashboard**: Angular → Gateway → Reporting (ready read model — fast, no fan-out to 3 services).
- **Daily snapshot**: MarketData finishes its sync → publishes `DailyPricesSynced` → Reporting fetches positions from Portfolio (REST), prices from MarketData (REST), computes and stores snapshots.
- **Rebalancing**: Strategy → Portfolio (positions) + MarketData (prices) synchronously, computed on demand.

## Data

- **Database per service** — physically: separate databases in **one PostgreSQL instance** (cost!), with separate DB users that have no access to other databases. Cross-DB joins are forbidden — data exchanged only via APIs/events.
- Cross-service references **by ID without FK** (e.g. `Asset.InstrumentId` is a Guid from MarketData — consistency enforced at the API level, not in the database). A conscious cost of microservices.
- Reporting maintains a **local read model** (copies of needed data built from events) — hands-on CQRS and eventual consistency.

## Orchestration and environments

| Environment | Tool |
|---|---|
| Local | **.NET Aspire** (AppHost: all services + PG + RabbitMQ, dashboard with traces/logs out of the box) — press F5 and everything is up |
| Production (start) | docker compose on a VPS (min. **8 GB RAM** — RabbitMQ + 6–7 .NET containers + PG) |
| Production (learning, Phase 5) | migration to **k3s** (lightweight Kubernetes) — only once compose starts to hurt |

## Observability (not optional in microservices)

- **OpenTelemetry from day 1** in all services (traces + logs + metrics), W3C `traceparent` propagation over HTTP and RabbitMQ (MassTransit handles it on its own).
- Locally: Aspire dashboard. Production: from Phase 2 Grafana + Tempo + Loki + Prometheus (one compose file).
- Health checks (`/health/live`, `/health/ready`) in every service; correlation id in error responses.

## Technology stack

| Layer | Choice | Notes |
|---|---|---|
| Services | .NET 10 LTS, ASP.NET Core Minimal APIs | LTS until Nov 2028 |
| Gateway | YARP | the standard in the .NET ecosystem |
| ORM | EF Core 10 + Npgsql, DbContext per service | global query filter on `UserId` |
| Broker | RabbitMQ + MassTransit v8 (or Rebus) | outbox + inbox |
| Jobs | Quartz.NET (in MarketData and Reporting) | persistence in PG |
| Auth | ASP.NET Identity + JWT in the Identity service; Gateway validates the signature | OpenIddict when OAuth becomes needed |
| Frontend | Angular 22, standalone components, signals; TS client generated from OpenAPI (per service, through the Gateway) | |
| UI kit | Angular Material or PrimeNG (ADR-010, spike in Phase 0) | |
| Tests | xUnit + Testcontainers (PG, RabbitMQ); event contract tests; Playwright e2e through the Gateway | |
| CI/CD | GitHub Actions: path filters per service → build/test/image only for changed ones | monorepo |
| Secrets | user-secrets locally; `.env` on the VPS; never in the repo | |

**Note on MediatR:** unchanged — we don't use it (commercial license from v13, unnecessary with Minimal APIs + slices).

## Key design decisions (unchanged from the previous version)

- Money: `decimal`, `Money` value object, PLN base currency, NBP rates in `marketdata_db` (auditable valuations).
- Multi-tenancy: isolation by `UserId` from the JWT, global query filter in every service, mandatory isolation tests.
- Prices are never fetched from external APIs in the request path — only jobs (Quartz) → our own database. Sources: NBP (currencies, gold), Stooq (GPW/ETFs/metals), CoinGecko (crypto), details in `03-domain-model.md`.

## What we deliberately do NOT do (at the start)

- Service discovery (Consul etc.) — addresses from configuration/compose DNS are enough.
- Saga/process manager — no flow requires it; if one appears, that's a great moment to learn.
- Separate Redis/cache — measure first, cache later.
- Kubernetes from day 1 — k3s only in Phase 5, once everything runs on compose.

## Diagrams

`diagrams/`: system context, services and communication, ERD (with data ownership marked), price sync sequence with events.
