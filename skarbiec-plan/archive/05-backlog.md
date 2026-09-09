# 05 — Backlog (epics and user stories)

Priorities: **M** (must, MVP) / **S** (should, v1.0) / **C** (could, later). Acceptance criteria are abbreviated — expand them when picking a story up for implementation.

## E1. Accounts and security
- [M] As a user I register with e-mail and password. *AC: password validation, Argon2/Identity default hash, e-mail confirmation may be S.*
- [M] I log in and stay logged in. *AC: JWT 15 min + refresh 30 days, logout invalidates the refresh token.*
- [M] I see only my own data. *AC: integration test — user B gets 404 on user A's resources.*
- [S] I change my password; I reset a forgotten password via e-mail.
- [C] 2FA (TOTP).

## E2. Portfolios and assets
- [M] I create/edit/archive portfolios. *AC: archiving instead of hard delete when assets exist.*
- [M] I add an asset of any class with manual valuation. *AC: currency, quantity or value, valuation date.*
- [M] I add a listed asset by picking an instrument from an autocomplete list. *AC: ticker search, last price preview.*
- [S] I add my own instrument (Stooq ticker / CoinGecko id) if it's missing from the list.

## E3. Transactions
- [M] I record a buy/sell/deposit/withdrawal/dividend/interest. *AC: asset quantity recalculated from transactions; selling more than the position = error.*
- [M] I edit and delete a transaction with position recalculation.
- [S] I import transactions from CSV (XTB + generic format). *AC: preview before saving, deduplication, per-row error report.*
- [C] Export of all data (CSV/JSON).

## E4. Market data
- [M] The system fetches currency rates and the gold price from NBP daily. *AC: upsert, retry, non-trading day = no entry, not an error.*
- [M] The system fetches quotes from Stooq and CoinGecko daily for instruments in use. *AC: only instruments attached to assets; one failure doesn't stop the rest.*
- [M] History backfill when an instrument is added (min. 1 year back if the source allows).
- [S] I see the date and source of the last price; a "stale" marker when > 7 days old.
- [S] Manual sync trigger via a button.

## E5. Valuation and dashboard
- [M] I see my net worth in PLN and a breakdown per asset class / per portfolio. *AC: pie chart, amounts and %.*
- [M] I see a net-worth history chart (1M/1Y/YTD/MAX). *AC: data from snapshots, gaps interpolated visually.*
- [S] I see profit/loss per asset (value − invested).
- [C] Simplified portfolio rate of return (TWR) yearly.

## E6. Strategy: allocation and rebalancing
- [S] I define a target allocation (asset classes, %, tolerance band). *AC: sums to 100%, validation.*
- [S] I see deviations of the current allocation from the target. *AC: color when outside the tolerance band.*
- [S] I get amount-based rebalancing suggestions. *AC: "sell X for N PLN, buy Y for M PLN"; disclaimer that this is not investment advice.*
- [S] "I'm depositing X PLN — where should it go?" mode (rebalancing with contributions, no selling).
- [C] E-mail notification when leaving the tolerance band.

## E7. Emergency fund and goals
- [S] I configure the emergency fund: monthly expenses, target months, designated assets. *AC: % coverage on the dashboard.*
- [S] I create a savings goal (amount, deadline). *AC: % progress, required monthly contribution at a given rate of return.*
- [C] Notification when the fund drops below a threshold / a goal is reached.

## E8. Operations and quality (ongoing, not features)
- [M] CI (monorepo, path filters): build + tests only for changed services; compose deploy with a single command.
- [M] Daily backup of all databases (pg_dump); documented and tested restore procedure.
- [M] Slice integration tests on Testcontainers (PG + RabbitMQ); tenancy isolation tests in every service.
- [M] OpenTelemetry in all services; a trace passes Gateway→service→event→consumer.
- [S] Grafana + Tempo + Loki + Prometheus in production; alert when PriceSyncJob hasn't run for 2 consecutive days.

## E9. Microservices platform (Phase 0)
- [M] As a developer I start the whole system with a single F5. *AC: Aspire AppHost — services + PG (databases per service) + RabbitMQ + dashboard.*
- [M] Angular talks only to the Gateway. *AC: YARP routes per prefix; JWT validated at the gateway; 401 without a token.*
- [M] Services publish events through the outbox. *AC: event stored in the same transaction as the data; published after commit; test: killing the process between commit and publish doesn't lose the event.*
- [M] Consumers are idempotent. *AC: delivering an event twice doesn't duplicate data (inbox/dedup by MessageId).*
- [M] Shared `ServiceDefaults`. *AC: OTel, health checks (/health/live, /health/ready), auth, HTTP resilience in one package, used by every service.*
- [S] Event contracts versioned additively — **deferred while ADR-019 (greenfield mode) holds**; records are edited in place instead. *AC while deferred: the deserialization contract test (unknown extra fields tolerated). Full AC (`V2` type on a breaking change) returns when ADR-019 is revoked.*
- [S] gRPC on the Strategy→Portfolio route (Phase 3, exercise).
- [C] Migration compose → k3s (Phase 5).
