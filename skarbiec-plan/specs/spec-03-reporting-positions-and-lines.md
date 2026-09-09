---
title: Reporting positions and valuation lines
status: draft
tier: 2
branch: feat/reporting-positions-and-lines
created: 2026-09-06
---

## Goal

Reporting keeps its own `Position` read model fed by spec-02's events, values it into per-asset `AssetValuation` lines plus a `ValuationSnapshot` per portfolio, and never calls Portfolio again.

## Why

Closes the first two rows of `architecture.md` §"Current vs target" and makes ADR-021 real:

- Today `DailyPricesSyncedConsumer` pulls every user's positions from Portfolio over REST (`GetPositionsForValuation` + `PortfolioPositionsClient`, `SystemCaller`) on every sync — snapshot computation depends on Portfolio's uptime and latency for data an event already carries in full.
- `ValuationSnapshot.BreakdownJson` is a JSONB blob that can only be read back whole; P/L per asset, emergency-fund and goal math (`domain.md` §Reporting) all need per-asset rows that nothing recomputes from scratch.

**Depends on spec-02** — the events consumed here do not exist before it merges.

## Scope

### Backend

**`Data/`**

- `Position` (new, `IUserOwned`): `AssetId` (primary key — globally unique, no surrogate needed, matches `domain.md`'s ERD), `PortfolioId`, `UserId`, `AssetClass`, `ValuationMode`, `InstrumentId?`, `Currency`, `Quantity`, `ManualValueAmount?`, `ManualValueDate?`, `PortfolioIsArchived`, `Version`, `UpdatedAt`. Index on `PortfolioId`.
- `AssetValuation` (new, `IUserOwned`): `Id, UserId, PortfolioId, AssetId, Date, AssetClass, Quantity, PriceUsed?, PriceDate?, FxRateUsed?, ValuePln, IsStale`; unique `(AssetId, Date)`; index `(UserId, Date)` for the dashboard breakdown.
- `ValuationSnapshot`: `− BreakdownJson`. `Data/ValuationBreakdown.cs` deleted.

**`Valuation/`** — the algorithm keeps its three modes, last-known price and 7-day stale rule (`domain.md` §Valuation algorithm) but now returns lines instead of a pre-aggregated breakdown: `ValuationPosition` gains `AssetId`; `Calculate` returns `ValuationResult { IReadOnlyList<ValuedPosition> Lines, decimal TotalPln, bool IsStale }` where the last two are derived from the lines; new `ValuedPosition { AssetId, AssetClass, Quantity, PriceUsed?, PriceDate?, FxRateUsed?, ValuePln, IsStale }`; `AssetClassBreakdownEntry` deleted (the breakdown is now a `GROUP BY AssetClass` over `AssetValuation`).

**`Messaging/`** — four new consumers, each with its own `sealed class XConsumerDefinition : IdempotentConsumerDefinition<XConsumer, ReportingDbContext>;` and registered through `AddRabbitMqMessaging(configureConsumers: …)` in `Program.cs` (the arity-1 `AddConsumer<T>(typeof(TDefinition))` form — `.claude/rules/messaging.md`). All of them read with `IgnoreQueryFilters()` and set `UserId` from the event: a consumer has no request user (`ICurrentUser.UserId` is `Guid.Empty`), which is exactly what `UserOwnedSaveInterceptor`'s "already set" escape hatch exists for — it **stays**, with its doc comment extended to name these consumers.

- `AssetPositionChangedConsumer`: upsert `Position` by `AssetId`. **Ignore the event when `message.Version < stored.Version`** (an out-of-order redelivery must not resurrect an older quantity); an equal `Version` is the same state, so applying it is harmless.
- `AssetRemovedConsumer`: delete the `Position`. Historical `AssetValuation` lines stay — they are already-computed history, and a dangling `AssetId` is a normal state (ADR-003).
- `PortfolioArchivedConsumer` / `PortfolioRestoredConsumer`: set `PortfolioIsArchived` on every `Position` of that portfolio (belt and braces next to spec-02's per-asset fan-out — either message alone leaves the read model correct).
- `PortfolioDeletedConsumer`: delete that portfolio's `Position`, `AssetValuation` **and** `ValuationSnapshot` rows — see Design decisions.

`DailyPricesSyncedConsumer` loses `IPositionsClient` and reads `db.Positions.IgnoreQueryFilters().Where(p => !p.PortfolioIsArchived)` instead (archived portfolios are excluded from valuation today too — behavior preserved). It still fetches prices and FX from MarketData's `prices/latest-batch` + `fx/latest-batch` through `IPriceQuoteClient` with `SystemCaller` auth — unchanged, this is the one surviving REST batch (ADR-021). It writes one `AssetValuation` per position (upsert by `(AssetId, Date)`) and one `ValuationSnapshot` per portfolio (upsert by `(PortfolioId, Date)`) in the consumer's single `SaveChangesAsync`, keeping the existing per-portfolio try/catch isolation.

**Features** — `GetDashboard` keeps totals, `AsOf`, `IsStale` and `ByPortfolio` from the latest `ValuationSnapshot` per portfolio, and builds `ByAssetClass` by grouping `AssetValuation` over the same `(PortfolioId, Date)` pairs. `GetNetWorthHistory` is untouched (it only ever read `TotalPln`). **Both response shapes are unchanged, so the Angular dashboard needs no edit and Reporting's generated client has no schema delta.**

**Deletions** — Portfolio: `Features/GetPositionsForValuation/*`, its endpoint mapping, handler registration and `GetPositionsForValuationEndpointTests.cs`; `SystemCaller` itself stays in ServiceDefaults for MarketData's batch endpoints, only Portfolio's use of it goes. Reporting: `Portfolio/IPositionsClient.cs`, `Portfolio/PortfolioPositionsClient.cs`, `Portfolio/PositionForValuation.cs`, the typed-`HttpClient` registration in `Program.cs`, `Tests/Fixtures/FakePositionsClient.cs`. `Skarbiec.AppHost/AppHost.cs`: the `.WithReference(portfolioService).WaitFor(portfolioService)` pair on `reportingService` and the comment above it. `requests/portfolio.http`: the positions-for-valuation request.

### Frontend

No component or template changes. `npm run gen:api` must be run and committed: Reporting's client comes back byte-identical, Portfolio's loses `getApiPortfolioPositionsForValuation`, `PositionsForValuationPage` and `PositionForValuationResponse` — nothing in `web/src/app` imports them (verify with `git grep -n PositionsForValuation -- web/src/app` outside `web/src/app/api`).

## Out of scope

- Insight features (`TargetAllocation`, `EmergencyFund`, `SavingsGoal`) — they are the reason `AssetValuation` exists, but they land in their own spec.
- MarketData's `InstrumentUsage` consumer of the same events, the currency catalog and `FxSyncJob` (spec-04).
- Backfilling `Position` for assets that existed before this spec: greenfield (ADR-019) — reset the local databases and re-add, or wait for the next mutation of each asset.
- The pre-existing behavior that an **archived** portfolio's last snapshot still counts toward net worth (`GetDashboard` takes the latest row per portfolio). Unchanged here; only *deleted* portfolios are cleaned up.
- Squashing Reporting's migrations (spec-06).

## Design decisions

1. **`PortfolioDeleted` deletes valuation history, not just positions.** `GetDashboard` sums the latest snapshot per portfolio, so leaving a deleted portfolio's rows behind would keep a ghost value in net worth forever. Portfolio only allows deleting an empty portfolio, so this normally removes little — and it also sweeps up rows orphaned by a lost `AssetRemoved`.
2. **`AssetRemoved` keeps the lines.** Deleting an asset must not silently change what last month's net worth was; `Position` is current state, `AssetValuation` is history.
3. **`Position.AssetId` is the primary key.** One row per asset by definition; a surrogate `Id` would only add a second uniqueness rule to enforce.
4. **Version comparison, not timestamps.** Clocks across a publisher and a consumer are not a total order; spec-02's per-asset counter is, and comparing it is a single integer test in the upsert path.
5. **Consumers write through the change tracker + one `SaveChangesAsync`,** never `ExecuteUpdate`/`ExecuteDelete` — the write must sit in the same transaction as the inbox row that makes it idempotent (ADR-012).
6. **`ValuationAlgorithm` stays pure.** It gains lines and loses the breakdown, but keeps zero I/O, so the mode/staleness matrix stays a table-driven unit test with no containers.
7. `architecture.md` §"Current vs target" rows 1–2 and `domain.md` §Reporting drop their "(target — spec-03)" markers in this PR; ADR-021 flips 🕐 → ✅.

## Data / API changes

| Entity / endpoint | Change | Migration? |
|---|---|---|
| `Position` | new table, PK `AssetId`, index `PortfolioId` | `PositionsAndAssetValuations` |
| `AssetValuation` | new table, unique `(AssetId, Date)`, index `(UserId, Date)` | `PositionsAndAssetValuations` |
| `ValuationSnapshot` | `− BreakdownJson` (jsonb column dropped) | `PositionsAndAssetValuations` |
| `GET /api/reporting/dashboard`, `/net-worth-history` | response shapes unchanged; breakdown now sourced from `AssetValuation` | — |
| `GET /api/portfolio/positions-for-valuation` | removed | — |

## Acceptance criteria

1. Given an `AssetPositionChanged` for an unknown asset, when it is consumed, then a `Position` row appears carrying every field of the event plus `UpdatedAt`. — proof: `AssetPositionChangedConsumerTests.Consume_NewAsset_InsertsPositionWithFullState`
2. Given a stored `Position`, when a later event for the same `AssetId` arrives, then the row is updated in place (still exactly one row) with the new `Quantity` and `Version`. — proof: `AssetPositionChangedConsumerTests.Consume_SecondEventForSameAsset_UpsertsSingleRow`
3. Given a stored `Position` at `Version` 5, when an event with `Version` 3 arrives, then the stored row is left untouched. — proof: `AssetPositionChangedConsumerTests.Consume_LowerVersionThanStored_IgnoresStaleEvent`
4. Given the same `MessageId` delivered twice, when both are consumed, then the consumer body's effect appears once. — proof: `AssetPositionChangedConsumerTests.Consume_SameMessageIdTwice_AppliesOnce`
5. Given a `Position` and its historical `AssetValuation` lines, when `AssetRemoved` is consumed, then the position is gone and the lines remain. — proof: `AssetRemovedConsumerTests.Consume_AssetRemoved_DeletesPositionAndKeepsValuationHistory`
6. Given two positions in one portfolio, when `PortfolioArchived` then `PortfolioRestored` are consumed, then `PortfolioIsArchived` flips to true and back on both. — proof: `PortfolioLifecycleConsumerTests.Consume_PortfolioArchivedThenRestored_FlipsFlagOnEveryPosition`
7. Given positions, lines and snapshots for a portfolio, when `PortfolioDeleted` is consumed, then all three are removed for that portfolio and no other portfolio is touched. — proof: `PortfolioLifecycleConsumerTests.Consume_PortfolioDeleted_RemovesPositionsAndValuationHistory`
8. Given local positions for two users and **no Portfolio HTTP client in the container**, when `DailyPricesSynced` is consumed, then a `ValuationSnapshot` per portfolio is written with the correct `TotalPln` and `UserId`. — proof: `DailyPricesSyncedConsumerTests.Consume_DailyPricesSynced_ValuesEveryPortfolioFromLocalPositions`
9. Given a position in an archived portfolio, when the sync is consumed, then no snapshot or line is written for that portfolio. — proof: `DailyPricesSyncedConsumerTests.Consume_DailyPricesSynced_SkipsArchivedPortfolios`
10. Given one market, one manual and one currency-valued position, when the sync is consumed, then one `AssetValuation` per position carries `Quantity`, `PriceUsed`/`PriceDate` (market only), `FxRateUsed` and `ValuePln`, and their sum equals the snapshot's `TotalPln`. — proof: `DailyPricesSyncedConsumerTests.Consume_DailyPricesSynced_WritesOneLinePerPositionSummingToTheSnapshot`, `ValuationAlgorithmTests.Calculate_ThreeValuationModes_ProducesOneLineEach`
11. Given a quote or FX rate older than 7 days, when the sync is consumed, then that line and its snapshot are `IsStale`, and a fresh sibling line is not. — proof: `DailyPricesSyncedConsumerTests.Consume_WithQuoteOlderThanSevenDays_MarksOnlyThatLineAndTheSnapshotStale`
12. Given the same `DailyPricesSynced` delivered twice, when both are consumed, then there is still one snapshot per portfolio and one line per asset for that date. — proof: `DailyPricesSyncedConsumerTests.Consume_SameMessageIdTwice_LeavesOneSnapshotAndOneLinePerAsset`
13. Given seeded snapshots and lines, when the dashboard and history endpoints are called, then their existing facts pass unchanged. — proof: `GetDashboardEndpointTests` (`Get_LatestSnapshotPerPortfolio_AggregatesNetWorthAndBreakdowns`, `Get_UsesLatestDatePerPortfolio_NotOneGlobalLatestDate`, `Get_WithPortfolioIdFilter_ReturnsOnlyThatPortfolio`, `Get_SurfacesStaleFlag_WhenAnyPortfolioSnapshotIsStale`, `Get_WithNoSnapshots_ReturnsZeroAndEmptyBreakdowns`), all `GetNetWorthHistoryEndpointTests` facts, and `GetNetWorthHistoryPerformanceTests.HandleAsync_OnSeededYearAcrossThreePortfolios_CompletesUnderOneSecond`
14. Given another user's positions, lines and snapshots, when this user calls the dashboard, then none of them appear in any total or breakdown entry. — proof: `GetDashboardEndpointTests.Get_NeverIncludesAnotherUsersSnapshots`, `GetDashboardEndpointTests.Get_NeverIncludesAnotherUsersAssetValuations`, `ArchitectureTests.DataNamespaceEntities_ImplementIUserOwned`
15. Given the merged branch, when Reporting's compiled assembly is inspected, then no type resides in `Skarbiec.Reporting.Portfolio` and no type name ends in `PositionsClient`. — proof: `ArchitectureTests.ReportingAssembly_ContainsNoPortfolioHttpClientTypes`
16. Given the Angular dashboard, when it renders against the unchanged response shapes, then its specs pass with no regenerated-client edits. — proof: `web/src/app/features/dashboard/dashboard.spec.ts` → `'builds pie segments from the asset-class breakdown'`, `'shows the formatted net worth and the as-of date'`, `'shows a stale chip when the dashboard is stale'`

## Verification

```
node scripts/verify.mjs --projects Reporting,Portfolio,Contracts --web --api
```

Manual smoke: `dotnet run --project Skarbiec.AppHost`, log in, add an asset with a quantity (via its initial transaction), press **Sync now** on Settings, then open the dashboard — net worth and the asset-class breakdown show the new value, and the Aspire trace for the sync shows Reporting calling **only** MarketData (`prices/latest-batch`, `fx/latest-batch`), never Portfolio.

## Risks / open questions

- `AssetValuation` grows at one row per asset per day. At personal scale (tens of assets) that is thousands of rows a year and needs nothing; a retention or roll-up policy is a later decision, not part of this spec.

## Result
