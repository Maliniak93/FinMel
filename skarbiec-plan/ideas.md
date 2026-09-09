# Ideas

Future features and platform work, condensed from the old backlog and roadmap (`archive/05-backlog.md`, `archive/zadania/phase-3..5-*.md`). One line each. Pick one up with `/design <idea>` when it's time — that turns a line here into a spec.

Tier is a starting guess (see `workflow.md` → Tier rule); `/design` sets the real one.

## Insights (Reporting, after spec-03's `AssetValuation` lines exist)

- **Target allocation** (tier 2) — `TargetAllocation`/`TargetAllocationLine` CRUD, per-portfolio or total-wealth scope, percentages sum to 100 with a tolerance band.
- **Allocation deviations** (tier 1; depends: target allocation) — current vs. target % per asset class from `AssetValuation`, flagging positions outside the tolerance band.
- **Rebalancing suggestions** (tier 2; depends: deviations) — asset-class-level buy/sell PLN amounts that close the drift; disclaimer text served from the backend, never hardcoded in the UI.
- **Contribution plan** ("I'm depositing X — where?") (tier 1; depends: rebalancing) — buy-only split of a new contribution toward target, never a sell.
- **Emergency fund** (tier 2) — `EmergencyFund` + designated assets, monthly-expenses × target-months coverage %, flags a designated asset later removed from Portfolio.
- **Savings goals** (tier 2) — `SavingsGoal` + linked assets, progress %, required monthly contribution from the annuity formula.

## Data

- **P/L per asset** (tier 1; depends: spec-03) — value minus invested, from `AssetValuation` plus transaction cost basis.
- **CSV import — XTB** (tier 2) — parse XTB's cash-operations/closed-positions export into transaction candidates; preview, dedup, per-row errors, atomic commit.
- **CSV import — generic format** (tier 1; depends: XTB import, for the shared preview/commit flow) — a documented generic column format as the escape hatch for any other broker.
- **Export (CSV/JSON)** (tier 1) — stream all user-owned entities per service; the transactions CSV round-trips through the generic importer.
- **Annual report — simplified TWR** (tier 2; depends: spec-03) — sub-period returns between external cash flows, contributions-vs-growth split; "simplified, not audit-grade" caveat in the UI.

## Platform

- **Notifications** (tier 2) — a consumer inside Reporting plus an `IEmailSender` abstraction, no new service; starts with allocation-drift and emergency-fund-below-threshold e-mails, deduplicated per user per day.
- **Playwright e2e smoke** (tier 1) — register → login → add asset → see valuation, through the Gateway, running in CI.
- **Per-service test databases** (tier 1) — split the shared Testcontainers Postgres into one container per service test run, if cross-service test flakiness ever shows up.
- **gRPC exercise: Reporting → MarketData `latest-batch`** (tier 2) — optional; the only gRPC candidate left standing after ADR-022 withdrew it from the required plan.
- **VPS deploy (docker compose)** (tier 2) — the `deploy/deploy.sh` target, `.env` secrets, the compose half of `deploy/README.md`; currently deferred, development stays local-only until this is picked up.
- **Backups** (tier 1; depends: VPS deploy) — `pg_dump` per database, cron, a tested restore procedure.
- **Production observability** (tier 2; depends: VPS deploy) — Grafana + Tempo + Loki + Prometheus; alert when a sync job hasn't run in 2 days.
- **k3s migration** (tier 2; depends: VPS deploy, backups) — only once docker compose actually starts to hurt (ADR-014) — not before.

## Later (no near-term commitment)

- **Liabilities** — a `Liability` entity symmetric to `Asset`; net worth becomes assets minus liabilities.
- **PIT-38 / FIFO** — yearly tax helper: FIFO lot matching, cost basis in PLN at the NBP rate of the preceding day; a report, not advice.
- **Household** — a sharing level above `UserId`; needs its own ADR first, since it changes the tenancy model.
- **PWA / mobile** — installable, read-only offline dashboard; deliberately no offline writes.
- **Open banking** — automatic account balances via a PSD2 aggregator; parked for cost and regulatory reasons.
