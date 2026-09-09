# Architecture

Target state (the approved redesign, Część II). Four services + gateway — Strategy folds into Reporting (ADR-020). See `decisions.md` for the ADRs this implements and "Current vs target" below for what still separates `master` from this document.

## Services

| Service | Responsibility | Database | Publishes | Consumes |
|---|---|---|---|---|
| **Gateway** (YARP) | routing `/api/<service>/*`, JWT validation, rate limiting, CORS | — | — | — |
| **Identity** | registration, login, refresh/logout | `identity_db` | `UserRegistered` | — |
| **Portfolio** | portfolios, assets (3 valuation modes), transactions = source of truth for quantity | `portfolio_db` | `AssetPositionChanged`, `AssetRemoved`, `PortfolioArchived`, `PortfolioRestored`, `PortfolioDeleted` | — |
| **MarketData** | currency catalog, instruments, FX rates, quotes, sync jobs, ticker verification (ADR-018) | `marketdata_db` | `DailyPricesSynced` | `AssetPositionChanged`, `AssetRemoved` (→ `InstrumentUsage`) |
| **Reporting** | positions read model, valuations (snapshots + per-asset lines), dashboard, history, insights (allocation, rebalancing, emergency fund, goals) | `reporting_db` | later: `AllocationDriftDetected`, `EmergencyFundBelowThreshold` | `AssetPositionChanged`, `AssetRemoved`, `Portfolio*`, `DailyPricesSynced` |

Angular talks only to the Gateway (ADR-013).

## Communication rules (ADR-021)

- **Domain facts travel as events with full state** (event-carried state transfer), through the MassTransit EF outbox; consumers are idempotent (inbox, dedup by `MessageId`). A consumer never calls back to the publisher over REST to fill in what the event didn't carry.
- **REST between services survives in exactly two places**: (1) request-path validation — Portfolio → MarketData `GET instruments/{id}` when creating a market asset; (2) a once-daily batch — Reporting → MarketData `prices/latest-batch` + `fx/latest-batch`, called from the snapshot consumer. `SystemCaller` auth is scoped to these two MarketData endpoints only.
- **Insights are computed locally** in Reporting from `AssetValuation`, as of the last snapshot — no fan-out on page load; a "Sync now" button re-triggers the sync jobs.
- **gRPC is withdrawn from the plan** (ADR-022) — the Strategy→Portfolio route disappears along with Strategy itself. A Reporting→MarketData batch exercise remains a candidate in `ideas.md`, not a commitment.

## Events (`Skarbiec.Contracts.Events`)

| Event | Payload | Published when |
|---|---|---|
| `AssetPositionChanged` | `AssetId, PortfolioId, UserId, AssetClass, ValuationMode, InstrumentId?, Currency, Quantity, ManualValueAmount?, ManualValueDate?, PortfolioIsArchived, Version` | after **every** position mutation: add/update asset, record/update/delete transaction |
| `AssetRemoved` | `AssetId, PortfolioId, UserId` | remove asset |
| `PortfolioArchived` / `PortfolioRestored` / `PortfolioDeleted` | `PortfolioId, UserId` | the matching slice (`Restore` is a new slice — no "unarchive" exists today) |
| `DailyPricesSynced` | as today, plus `Kind: Prices \| Fx` | end of a sync job |
| `UserRegistered` | as today | no consumer yet (Notifications is an idea, not built) |

`AssetChanged` and `TransactionRecorded` are removed — today they have no consumers and don't carry full state. Contracts are edited in place, no `V2` types (ADR-019).

## Current vs target

| Today (`master`) | Target | Closed by |
|---|---|---|
| Reporting pulls positions from Portfolio over REST (`SystemCaller`) on every snapshot | Reporting keeps a local `Position` read model fed by `AssetPositionChanged`/`AssetRemoved`/`Portfolio*` | spec-02, spec-03 |
| `ValuationSnapshot.BreakdownJson` (JSONB) | per-asset `AssetValuation` lines; the breakdown becomes `GROUP BY AssetClass` over lines | spec-03 |
| `AssetChanged`/`TransactionRecorded` publish with no consumers; updating or deleting a transaction publishes nothing | replaced by `AssetPositionChanged`/`AssetRemoved` from every mutating slice | spec-02 |
| `PriceSyncJob` syncs every dictionary instrument | syncs only instruments with `InstrumentUsage.AssetCount > 0` | spec-04 |
| History backfill on first use of an *existing* instrument isn't wired | `HistoryBackfillJob` fires from the `InstrumentUsage` consumer on first use | spec-04 |
| No currency catalog; FX sync covers supported currencies ad hoc | `Currency` catalog + `FxSyncJob` (daily; 12-month backfill on first run) | spec-04 |
| Strategy service exists as an empty skeleton | removed; its future features live in Reporting | spec-01 |
| `Portfolio.AssetCount` / `Asset.TransactionCount` counters; unused `ApplicationUser.BaseCurrency` | removed — delete guards use `AnyAsync`; PLN is the only base currency everywhere (ADR-008) | spec-02, spec-05 |
| 3/5/7/1 migrations per service, carrying task-history names | one `InitialCreate` migration per service (ADR-019) | spec-06 |
| Worktree branch `worktree-marketdata-currency-redesign` (a currency catalog + `FxSyncJob` prototype) | superseded by spec-04's design (no fallback rate) | delete after spec-04 merges |

## Diagrams

### C4 context

```mermaid
flowchart LR
    U["User (browser)"]
    subgraph SKARBIEC["Skarbiec"]
        SPA["Angular 22 SPA"]
        GW["Gateway (YARP)"]
        SVC[".NET 10 services:<br/>Identity, Portfolio, MarketData, Reporting"]
        MQ["RabbitMQ"]
        DB[("PostgreSQL<br/>db per service")]
    end
    NBP["NBP API<br/>FX, gold"]
    STOOQ["Stooq<br/>GPW / ETF / metals"]
    CG["CoinGecko<br/>crypto"]

    U -->|HTTPS| SPA
    SPA -->|"REST/JSON + JWT"| GW
    GW --> SVC
    SVC <-->|events| MQ
    SVC --> DB
    SVC -->|"jobs, daily"| NBP
    SVC -->|"jobs, daily"| STOOQ
    SVC -->|"jobs, daily"| CG
```

### Container

```mermaid
flowchart TB
    SPA["Angular SPA"] -->|"/api/* + JWT"| GW["Gateway (YARP)"]
    GW --> ID["Identity"]
    GW --> PF["Portfolio"]
    GW --> MD["MarketData"]
    GW --> RP["Reporting"]

    PF -->|"REST: validate instrument"| MD
    RP -->|"REST: prices/fx batch, daily"| MD

    ID -->|"event: UserRegistered"| MQ["RabbitMQ"]
    PF -->|"events: AssetPositionChanged,<br/>AssetRemoved, Portfolio*"| MQ
    MD -->|"event: DailyPricesSynced"| MQ
    MQ -->|consumes| MD
    MQ -->|consumes| RP

    ID --> IDB[("identity_db")]
    PF --> PDB[("portfolio_db")]
    MD --> MDB[("marketdata_db")]
    RP --> RDB[("reporting_db")]

    MD -->|"HTTP, jobs"| EXT["NBP / Stooq / CoinGecko"]

    ASPIRE["Aspire dashboard<br/>(traces, logs, metrics)"]
    ASPIRE -.-> GW & ID & PF & MD & RP
```

Solid = REST/HTTP (Gateway routing and the two narrow inter-service exceptions above); `event:`-labeled = RabbitMQ/MassTransit; dotted = Aspire telemetry, not a data dependency.

### Sequence: asset mutation → position + usage

```mermaid
sequenceDiagram
    autonumber
    participant U as Angular
    participant PF as Portfolio
    participant MQ as RabbitMQ
    participant RP as Reporting
    participant MD as MarketData

    U->>PF: add/update asset or transaction
    PF->>PF: write + AssetPositionChanged (same tx, outbox)
    PF->>MQ: publish (from outbox, after commit)
    MQ->>RP: deliver AssetPositionChanged
    RP->>RP: inbox check, upsert Position
    MQ->>MD: deliver AssetPositionChanged
    MD->>MD: inbox check, upsert InstrumentUsage
    alt first use of this instrument
        MD->>MD: enqueue HistoryBackfillJob
    end
```

### Sequence: sync jobs → snapshot

```mermaid
sequenceDiagram
    autonumber
    participant Q as Quartz in MarketData
    participant MD as MarketData
    participant EXT as NBP / Stooq / CoinGecko
    participant MQ as RabbitMQ
    participant RP as Reporting

    Q->>MD: FxSyncJob (all catalog currencies) + PriceSyncJob (AssetCount > 0 only)
    MD->>EXT: fetch rates / quotes
    MD->>MD: upsert + outbox DailyPricesSynced (Kind: Fx | Prices)
    MD->>MQ: publish
    MQ->>RP: deliver DailyPricesSynced
    RP->>RP: inbox check
    RP->>MD: REST prices/latest-batch + fx/latest-batch
    MD-->>RP: batch prices/rates
    RP->>RP: value local Positions → AssetValuation lines +<br/>ValuationSnapshot (last-known price, stale > 7 days)
```

### Sequence: login → refresh

```mermaid
sequenceDiagram
    autonumber
    participant U as Angular
    participant GW as Gateway
    participant ID as Identity

    U->>GW: POST /api/identity/login
    GW->>ID: forward
    ID-->>U: 15-min JWT + rotated refresh token (httpOnly cookie)
    Note over U: JWT expires
    U->>GW: POST /api/identity/refresh (cookie)
    GW->>ID: forward
    ID->>ID: validate + rotate refresh token
    ID-->>U: new JWT + Set-Cookie
```

### Sequence: add market asset with ticker verification

```mermaid
sequenceDiagram
    autonumber
    participant U as Angular
    participant PF as Portfolio
    participant MD as MarketData

    U->>PF: add asset (ticker T)
    PF->>MD: GET instruments?ticker=T
    alt dictionary hit (Verified)
        MD-->>PF: instrument, no external call
    else unknown ticker
        MD->>MD: ITickerVerifier → provider (~5s timeout, no retry)
        alt Exists
            MD-->>PF: instrument created Unverified
        else DoesNotExist
            MD-->>PF: 400 — blocks creation
        else Unreachable
            MD-->>PF: warning + explicit opt-in → Unverified
        end
    end
    PF->>PF: create asset, publish AssetPositionChanged
```

## Local orchestration

.NET Aspire (`Skarbiec.AppHost`) runs Postgres (one instance, a database + scoped role per service), RabbitMQ, every service and the Gateway, with a dashboard for traces/logs/metrics. `dotnet run --project Skarbiec.AppHost` is the F5 entry point.

## Observability

OpenTelemetry in every service (traces, logs, metrics), W3C `traceparent` propagated over HTTP and RabbitMQ (MassTransit does this automatically). Aspire dashboard locally; `/health/live` and `/health/ready` in every service. Production observability (Grafana/Tempo/Loki/Prometheus) is an idea, not built — see `ideas.md`.

## Deployment status

VPS deployment (docker compose) is deferred — `deploy/README.md` documents local Aspire only; the compose/VPS half of `deploy/` doesn't exist yet. Development and CI both run local-only.

## What we deliberately do not do

- Service discovery — addresses come from Aspire/compose DNS.
- A saga/process manager — no flow needs one yet.
- A separate cache (Redis) — measure first, cache later.
- Kubernetes, before docker compose actually starts to hurt (k3s is a later idea, not a plan).
- gRPC anywhere in the current plan (ADR-022).
