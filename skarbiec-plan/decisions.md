# Decisions

Abbreviated format. Status: ✅ accepted / 🕐 pending / ❌ rejected. ADR-001…019 are copied verbatim from the original plan (`archive/06-adr-decisions.md`) — their text is not rewritten, only a one-line "Amended by" note is added where a later ADR narrows one. ADR-020 onward implement the 2026-09 redesign (`architecture.md`, `domain.md`, `workflow.md`) and stay 🕐 pending until their spec ships, except ADR-024 (development model), which took effect immediately.

## ADR-001 ✅ Microservices (goal: learning) — revised

**Amended by ADR-020.**

**Context:** a modular monolith was originally recommended (1 developer). Microservices were chosen because **learning this architecture is an explicit goal of the project** — which changes the cost-benefit math.
**Decision:** 5 services + gateway (Identity, Portfolio, MarketData, Strategy, Reporting; Notifications in Phase 4). Monorepo. Vertical slices inside services.
**Consequences (consciously accepted):** eventual consistency, no FKs between services, more expensive hosting (8 GB VPS), slower MVP (+4–6 weeks), observability mandatory from day 1. No further service splitting without an ADR.

## ADR-002 ✅ Vertical Slice Architecture inside every service

**Decision:** feature = folder (endpoint + handler + validator). No Service/Repository layers. Shared code extracted on the third use.

## ADR-003 ✅ Database per service in a single PostgreSQL instance

**Decision:** a separate database per service (`identity_db`, `portfolio_db`, ...), separate DB users with no access to other databases, one PG instance (cost). Cross-DB joins forbidden; cross-service references by ID without FK.
**Consequences:** true data autonomy at the cost of a single instance; moving a database to a separate machine = a connection-string change.

## ADR-004 ✅ No MediatR

**Decision:** Minimal API endpoints call handlers directly from DI. (MediatR is commercial from v13; unnecessary with slices.)

## ADR-005 ✅ Identity service + JWT; Gateway validates the token

**Decision:** ASP.NET Identity in the Identity service; 15-minute access token + rotated refresh token in an httpOnly cookie. The Gateway (YARP) validates the signature and passes the JWT through (token passthrough) — downstream services read `UserId` from claims.
**Consequences:** no shared session; revocation = short TTL + refresh rotation.

## ADR-006 ✅ Multi-tenancy: isolation by UserId in every service

**Decision:** every user-owned entity has `UserId`; EF Core global query filter + save interceptor; `UserId` always from the JWT, never from the body. Isolation tests in every service's definition of done.

## ADR-007 ✅ Prices only from our own database (jobs in MarketData)

**Decision:** external APIs (NBP, Stooq, CoinGecko) are queried exclusively by Quartz jobs in MarketData; the rest of the system reads via the MarketData API or read models. Narrowed by ADR-018 (ticker verification only).

## ADR-008 ✅ PLN base currency, `decimal` everywhere

Unchanged. `Money` value object in `Skarbiec.Contracts`/SharedKernel per service.

## ADR-009 ✅ Transactions as the source of truth for asset quantity

Unchanged (Portfolio service). Data ready for FIFO/PIT-38.

## ADR-010 ✅ Angular Material as the UI kit for Phase 1+

**Context:** T0.16 spike, criteria: data tables (transactions), form components, theming effort, signals/zoneless compatibility, bundle size, maintenance signals. Mid-spike, PrimeNG's GitHub repo was archived (2026-06-28): PrimeTek moved future development to a closed model under "PrimeUI" — a free "Community License" tier (eligibility: <$1M revenue, <5 developers, <10 employees, <$3M VC funding, renewed annually) or a $599/developer commercial license; existing MIT releases stay MIT forever but get no further updates under that license. An unofficial community fork exists but is new and contested (trademark dispute with PrimeTek).
**Decision:** Angular Material + CDK. Officially maintained by the Angular team, so version parity is guaranteed as this project tracks bleeding-edge Angular releases (ADR requirement to verify APIs against current docs); zoneless + Signals support already shipped (CDK/Material since Angular 18, ahead of where PrimeNG's own signals-only rework — "PrimeNgX" — stood before the licensing change); smaller bundle footprint.
**Loser's dealbreaker (PrimeNG):** richer data table out of the box (sort/paginate/virtual-scroll/export) that would have fit the transactions-heavy UI well, but the project just went closed-source with an annual-renewal free tier and unproven long-term OSS continuity — an unacceptable governance risk for a project meant to run for years as a personal learning project.
**Consequences:** the Portfolio transactions table (Phase 1+) is built on CDK Table with `MatSort`/`MatPaginator`/manual filtering instead of a drop-in PrimeNG `p-table` — extra one-time wiring, acceptable given the project's explicit learning-architecture goal (ADR-001).

## ADR-011 ✅ Hosting: VPS min. 8 GB, docker compose

**Decision:** RabbitMQ + 6–7 .NET containers + PG won't reasonably fit in 4 GB. Hetzner/OVH ~8 GB. Compose until Phase 5, then optionally k3s (ADR-014).

## ADR-012 ✅ RabbitMQ + MassTransit v8, outbox + inbox

**Context:** MassTransit v9 moves to a commercial license; v8 remains OSS. Alternative: Rebus (lighter, OSS).
**Decision:** RabbitMQ as the broker; **MassTransit v8** (best documentation, built-in EF Outbox, inbox/dedup, retry, trace-context propagation). Events published exclusively through the outbox; consumers idempotent.
**Consequences:** pinned to v8 (no v9 upgrade); if v8 stops being maintained — migration to Rebus/Wolverine behind an `IEventBus` abstraction is realistic because the contracts are POCOs.
Contract-versioning clause suspended by ADR-019 (greenfield mode); outbox and consumer idempotency stand unchanged.

## ADR-013 ✅ YARP as the API Gateway

**Decision:** a single entry point: routing per prefix (`/api/portfolio/*` → Portfolio), JWT validation, rate limiting, CORS. No BFF at the start — Angular composes views from service responses, and where that hurts (the dashboard), the ready Reporting read model answers.

## ADR-014 ✅ .NET Aspire locally; compose in production; k3s in Phase 5

**Decision:** Aspire AppHost as local orchestration (services + PG + RabbitMQ + dashboard with traces). Compose in production; migration to k3s only as a deliberate exercise in Phase 5.
**Consequences:** F5 experience locally despite 7 processes; no need to learn k8s before delivering value.

## ADR-015 ✅ Communication: REST synchronously, events asynchronously, CQRS in Reporting

**Amended by ADR-021, ADR-022.**

**Decision:** "here and now" queries — internal REST with resilience (retry+jitter, circuit breaker, timeout); domain facts — events over RabbitMQ (`Skarbiec.Contracts`, additive versioning, breaking = `V2`). Reporting builds a local read model (dashboard without fan-out). gRPC will be introduced on one route (Strategy→Portfolio) in Phase 3+ as a learning exercise.
**Consequences:** dashboard is eventually consistent (acceptable: daily data); dual knowledge of the "truth" (Portfolio) and the "projection" (Reporting) — explicitly documented.
Contract-versioning clause ("additive versioning, breaking = `V2`") suspended by ADR-019 (greenfield mode).

## ADR-016 ❌ Service discovery, saga, Redis, K8s from day 1 — rejected at the start

**Decision:** service addresses from configuration (compose DNS / Aspire); no flows requiring a saga; caching only after measurements; k8s in Phase 5. Each of these tools comes back with its own ADR when a real need appears.

## ADR-017 ✅ Result pattern for handler error flow — no exceptions for expected failures

**Context:** Minimal API handlers, and validating factories on shared value objects (e.g. `Money`), need a uniform way to signal expected failures (not found, business-rule violation, conflict, invalid value) without throwing for control flow, while ProblemDetails stays the wire-level error shape.
**Decision:** Handlers, and value-object factories, return a `Result`/`Result<T>` (railway-oriented) instead of throwing for expected error paths. The lightweight `Result`/`Result<T>`/`Error` types live in `Skarbiec.Contracts` (it has zero solution-internal dependencies, so every project — including `Money`'s own validation — can reference them) *(default — confirm in Phase 0; alternative if the hand-rolled type gets unwieldy: `ErrorOr`/`FluentResults`)*. `Skarbiec.ServiceDefaults` adds one shared mapping helper (`Result` failure → `TypedResults`/ProblemDetails) that every service's endpoints call. Exceptions stay reserved for genuinely unexpected failures, still caught by the global `AddProblemDetails()` handler.
**Consequences:** every slice's handler signature is `Result`/`Result<T>` instead of throwing or returning `TypedResults` directly; the endpoint does the mapping; one shared ProblemDetails-mapping helper needed in ServiceDefaults (T0.3); error paths become assertable on `Result.IsFailure`/`Error` in tests without catching exceptions; `Money.Create(...)` (T0.2) is the first user of the pattern.

## ADR-018 ✅ Ticker verification may call a provider in the request path — a narrow exception to ADR-007

**Context:** a user adding a market asset must learn *before* the asset exists whether the ticker they typed is real at its provider (modification set 1, S6/M1.1). ADR-007 forbids exactly that: external APIs are queried only by Quartz jobs. T2.8 shipped the compliant alternative — create the instrument `Unverified`, let `HistoryBackfillJob` flip it to `Verified`/`Failed` off the request path — and its own scope allowed a documented "pragmatic exception" instead. Live probes on 2026-08-11 settled the trade-off: CoinGecko answers in ~250 ms and returns `{}` for an unknown id, while stooq.com now gates every CSV endpoint behind a JavaScript proof-of-work challenge (`/q/l/` → 404 HTML, `/q/d/l/` → challenge page), so a plain `HttpClient` cannot reach it at all. Create-then-poll therefore makes the user wait on a job only to be told "Failed" for every Stock/Etf/Bond ticker — a worse answer, delivered later, after the instrument row already exists.
**Decision:** MarketData may call a price provider **synchronously in a request path for exactly one purpose: verifying that a user-supplied ticker exists at its provider, before the instrument (and therefore the asset) is created.** Never for valuation, never to persist a `PriceQuote`/`FxRate` — prices still reach the database only through Quartz jobs, so ADR-007 stands unchanged for everything else. Containment is part of the decision, not an implementation detail:
- a ticker already in MarketData's `Instruments` table as `Verified` is confirmed **from the database, with no external call at all** — the exception only fires for a genuinely unknown ticker;
- the only abstraction a `Features/*` handler may depend on is `ITickerVerifier` in `Skarbiec.MarketData.Sources.Verification`; its implementation depends on `IPriceSource` and stays inside the `Sources` namespace, so the existing guardrail `ArchitectureTests.OnlySourcesNamespace_DependsOn_PriceSourceAbstractions` keeps passing unchanged, and a second architecture test restricts `ITickerVerifier` itself to the verification slice — that pair is what stops the exception spreading;
- request-path budget: one attempt, hard timeout (~5 s, linked `CancellationTokenSource`), **no rate-limit retry** — `CoinGeckoPriceSource.FetchWithRateLimitRetryAsync` sleeps for `Retry-After` and retries once, which is right for a job and wrong for a request;
- verification writes nothing to the database;
- three outcomes, reusing `PriceFetchResult`'s existing vocabulary rather than a bool: `Success` (≥1 quote) → **Exists**, `NoData` → **DoesNotExist**, `Error` → **Unreachable**. CoinGecko's `{}` already lands on `NoData` and Stooq's 404/challenge page already lands on `Error`, so no price source needs changing for the mapping to be honest.
**Consequences:** an asset-creation request now depends on a third party's latency (~250 ms CoinGecko; a full timeout for Stooq), bounded by the timeout above; CoinGecko's free tier is exposed to user-driven traffic, mitigated by the dictionary-first short circuit and by the UI verifying on blur/submit rather than per keystroke (the Gateway's 100 req/10 s limit applies too). `DoesNotExist` blocks creation outright; `Unreachable` warns and offers an explicit opt-in that creates the instrument `Unverified` exactly as T2.8 does, so `InstrumentVerificationStatus` keeps a single meaning and `HistoryBackfillJob` remains the thing that resolves it. Known constraint accepted with this ADR: while Stooq stays JS-gated, every Stock/Etf/Bond ticker outside the seeded dictionary resolves to `Unreachable` — repairing or replacing that source is separate work, not part of this exception.

## ADR-019 ✅ Greenfield mode — no backward compatibility until the data matters

**Context:** the system has exactly one user (the author), holds no real data, and runs local-only — every `[VPS]`/production task is deferred (`zadania/README.md`), so nothing has ever been deployed with a portfolio in it. Yet the project carries the full compatibility ceremony of a system with live consumers: additive-only event contracts with `V2` types for breaking changes (ADR-012, ADR-015, `CONTRACTS.md`, E9), and an implicit assumption that EF migration history is append-only and data-preserving. That ceremony costs work on every change and clutters the code while protecting consumers and rows that do not exist.
**Decision:** while greenfield mode holds, any change may be implemented **as if the feature were being written from scratch** — no legacy data, no legacy processes, no migration paths:
- event/DTO records in `Skarbiec.Contracts` are **edited in place** — fields renamed, retyped or removed outright; no `V2` types, no deprecation window, no two shapes coexisting. Every publisher and consumer of a changed record is updated in the same change;
- EF migrations may **drop columns/tables and change types with no data backfill**; when the history gets in the way, a service's migrations may be deleted and regenerated as a single `InitialCreate`, and the local databases dropped (Aspire recreates them);
- HTTP routes, request/response shapes and enum members change freely — the Angular client is regenerated (`npm run gen:api`);
- code, tests and documentation describing a replaced approach are **deleted, not deprecated**. A rewrite is a legitimate answer to "change X" when it produces a better design than an incremental patch.

**Unchanged by this ADR** — everything that is not about compatibility *over time*: tenancy isolation by `UserId` (ADR-006), the outbox and idempotent consumers (ADR-012 — duplicate delivery is a runtime property of the broker, not a legacy concern), the `Result` pattern (ADR-017), and the per-slice definition of done (tests green, zero warnings, `dotnet format`). Consumers still tolerate unknown fields (forward compatibility), guarded by the deserialization tests in `Skarbiec.Contracts.Tests`.
**Consequences:** the versioning clause of ADR-012 and ADR-015 is **suspended, not deleted** — the discipline is written down and comes back the day it is needed; backlog story E9 `[S]` (additive contract versioning) is deferred for the same reason. After pulling a destructive or squashed migration, local databases must be reset before running the app. **This ADR has no automatic expiry: it holds until the user explicitly revokes it** — at which point a new ADR supersedes it and reinstates the compatibility rules (the first real deployment carrying data worth keeping is the natural moment to do so).

---

## ADR-020 🕐 Four services — Strategy folded into Reporting

**Context:** Strategy was scaffolded as an empty skeleton in Phases 0–2; every feature planned for it (target allocation, rebalancing, emergency fund, savings goals) was always Phase-3+ scope and needs exactly the valuation data Reporting already owns. Running it as a fifth service earns no extra microservices-learning value that a vertical slice inside Reporting wouldn't also teach.
**Decision:** fold Strategy into Reporting. Four services + gateway: Identity, Portfolio, MarketData, Reporting. Strategy's project, tests, Gateway route, AppHost registration, CI job and dependabot entry are removed (spec-01). "Few services, many patterns" (ADR-001) stands unchanged — this narrows the count, not the principle.
**Consequences:** one less service to run, trace and deploy; insight features become vertical slices inside Reporting, computed directly against `AssetValuation` with no cross-service call for them at all. Amends ADR-001.

## ADR-021 🕐 Event-carried state transfer for positions; REST narrowed to validation + batch

**Context:** today Reporting fetches positions from Portfolio over REST (`SystemCaller`) on every snapshot cycle, via `GetPositionsForValuation` + `PortfolioPositionsClient` (decided in T2.11). This couples snapshot computation to Portfolio's availability and latency, and re-implements query logic an event could carry for free.
**Decision:** position facts travel as events carrying full state (`AssetPositionChanged`, `AssetRemoved`, `Portfolio*`) through the outbox; Reporting maintains a local `Position` read model via idempotent consumers (inbox) instead of querying Portfolio. REST between services narrows to exactly two uses: request-path validation (Portfolio → MarketData ticker/instrument lookup) and a once-daily prices/FX batch (Reporting → MarketData). Supersedes the T2.11 `positions-for-valuation` decision; narrows ADR-015.
**Consequences:** `GetPositionsForValuation` and `PortfolioPositionsClient` are deleted (spec-03); Reporting's snapshot computation no longer depends on Portfolio's uptime; the read model can lag by at most one event delivery — eventual consistency already accepted for a daily-granularity dashboard.

## ADR-022 🕐 gRPC withdrawn from the plan

**Context:** ADR-015 planned a Strategy→Portfolio gRPC route as a Phase-3 learning exercise. ADR-020 removes Strategy; ADR-021 removes the REST query gRPC would have replaced.
**Decision:** gRPC is withdrawn from the plan entirely — no route in the redesigned architecture uses it. A Reporting→MarketData `latest-batch` gRPC exercise remains a candidate in `ideas.md`, not a commitment.
**Consequences:** one less protocol/toolchain to stand up (proto codegen, HTTP/2 wiring, Aspire/compose config) for a service count that no longer needs it. Part of ADR-015.

## ADR-023 🕐 Currency catalog in MarketData + separate FxSyncJob, backfill instead of fallback

**Context:** worktree branch `worktree-marketdata-currency-redesign` prototyped a `Currency` catalog with a `FallbackRateToPln` field for currencies missing a fresh rate. Separately, the M1 modification set (`praca_2026-08-12`) solved the same underlying problem differently: `PriceSyncJob` syncs an FX rate for every `SupportedCurrencies` code, so a currency-valued asset's rate is never stuck on the seeder's 2020 bootstrap row. A fallback rate would be a second, weaker source of truth for a fact a proper backfill already fixes.
**Decision:** a `Currency` catalog lives in MarketData (`Code, Name, Symbol, DecimalPlaces, DisplayOrder`; seed PLN, EUR, USD, GBP, CHF) with no `FallbackRateToPln` and no `IsPivot` — PLN is the fixed base (ADR-008). A dedicated `FxSyncJob` runs daily for every catalog currency; its first run backfills 12 months of history. This absorbs the worktree branch's intent without its fallback-rate design.
**Consequences:** one FX code path instead of two (job sync + fallback); `worktree-marketdata-currency-redesign` is superseded — safe to delete once spec-04 ships; `SyncRun.Kind` gains `Backfill` so all three jobs (`FxSyncJob`, `PriceSyncJob`, `HistoryBackfillJob`) share one log shape.

## ADR-024 ✅ Development model — spec as the unit of work

**Context:** the task-list-per-phase model (`archive/zadania/phase-*.md`) served Phases 0–2 well as a learning scaffold, but doesn't fit a codebase past bring-up: it has no place for agent delegation, per-role model selection, or a deterministic pipeline — a human executed each task in order.
**Decision:** the spec is the unit of work (`skarbiec-plan/specs/<slug>.md`, shaped by `specs/_template.md`). `/design` interviews and writes a spec at `status: draft`; the user approves it to `status: approved` (Definition of Ready: every AC maps to a test or command, tier is set, no open questions remain). `/build <spec>` runs a deterministic script (`Workflow` — zero model tokens spent on control flow) through Tests → Implement → Verify → Review → Ship, delegating each phase to an agent whose model/effort matches that phase's difficulty (Haiku for mechanical verification, Sonnet for tests/implementation/ops, Opus for review and Tier-2 implementation). Author ≠ reviewer: the reviewer runs in a fresh context, adversarial to the spec, and returns structured findings, not prose. Ship always commits to `feat/<slug>` and opens a PR to `master`; the user always merges.
**Consequences:** git operations, PR merges and branch cleanup stay entirely the user's call — the orchestrator never merges. Definition of Done: verify green, review has no `blocking` findings, PR open, spec flipped to `status: done` with a PR link, and any rule/ADR the spec touched updated in the same change. Full operational detail lives in `workflow.md`.
