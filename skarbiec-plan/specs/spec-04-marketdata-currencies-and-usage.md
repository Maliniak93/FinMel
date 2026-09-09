---
title: MarketData currency catalog, FX sync job and instrument usage
status: draft
tier: 2
branch: feat/marketdata-currencies-and-usage
created: 2026-09-06
---

## Goal

MarketData owns a `Currency` catalog driving a dedicated `FxSyncJob`, and an `InstrumentUsage` read model built from Portfolio's position events, so `PriceSyncJob` syncs only instruments actually in use and the first use of an instrument triggers its history backfill.

## Why

Closes four `architecture.md` "Current vs target" rows: no currency catalog (FX synced ad hoc off `SupportedCurrencies`), `PriceSyncJob` syncing every dictionary instrument, backfill-on-first-use never wired (the T2.7/T2.9 gap), and three jobs with no shared run log. Implements ADR-023; absorbs `worktree-marketdata-currency-redesign` without its fallback-rate design.

## Scope

### Backend

- **MarketData** — new `Data/Currency.cs`, `Data/InstrumentUsage.cs`, `Data/AssetInstrumentLink.cs`, `Data/SyncRunKind.cs`; `SyncRun.Kind`; new `Sources/FxSyncJob.cs` + `Sources/FxSyncJobExtensions.cs`; new `Messaging/AssetPositionChangedConsumer.cs`, `Messaging/AssetRemovedConsumer.cs` (+ their `IdempotentConsumerDefinition`s) registered in `Program.cs`; `MarketDataSeeder` seeds currencies and stops seeding bootstrap FX rows; `PriceSyncJob` loses its FX block and filters by usage; `HistoryBackfillJob` loses its FX backfill and writes a `SyncRun`; `GetSyncStatus` returns the latest run per kind. Migration `CurrenciesAndInstrumentUsage`.
- **Contracts** — `DailyPricesSynced` gains `Kind`; new `Events/PriceSyncKind.cs` (`Prices | Fx`). In-place edit, no `V2` (ADR-019).
- **Reporting** — `DailyPricesSyncedConsumer` recomputes snapshots on both kinds.

### Frontend

`web/src/app/features/settings/*` only: the sync card shows the last Prices run and the last FX run. `npm run gen:api` after the `SyncStatusResponse` change.

## Out of scope

- Reporting's `Position` read model and `AssetValuation` lines (spec-03) — this spec does not touch how a snapshot is computed, only what triggers one.
- Currency-driven UI dropdowns: the user-facing set stays `SupportedCurrencies` (PLN/EUR/USD) in `Skarbiec.Contracts` and `web/src/app/shared/currencies.ts`. The catalog is deliberately wider and backend-only.
- Removing `IFxRateSource` from the `IPriceSource` implementations / restructuring `Sources` abstractions.
- Deleting `worktree-marketdata-currency-redesign` (the user's call, after this merges).

## Design decisions

1. **`Currency`** = `Code` (PK, `char(3)`, ISO uppercase), `Name`, `Symbol`, `DecimalPlaces` (default 2), `DisplayOrder`. No `FallbackRateToPln`, no `IsPivot` — PLN is the fixed base (ADR-008, ADR-023). Seeded PLN/EUR/USD/GBP/CHF with English names, `DisplayOrder` 0..4, idempotent by `Code`. It must be a superset of `SupportedCurrencies.All`, asserted by a test rather than a constraint.
2. **Bootstrap FX rows deleted** from `MarketDataSeeder` (`SeedFxRates`, `BootstrapDate`). `FxSyncJob`'s 12-month backfill replaces them; `MarketDataSeederTests` asserts the seeder writes no `FxRate` row at all.
3. **`FxSyncJob`** — `[DisallowConcurrentExecution]`, `JobKey("fx-sync", "market-data")`, `ActivitySourceName = "Skarbiec.MarketData.FxSyncJob"`, activity `FxSyncJob.Run`, Quartz-independent `RunAsync(CancellationToken)` entry point, registered in `Program.cs`'s `AddSource(...)`. Cron from `FxSync:Cron`, production default `0 0 13 ? * MON-FRI` (after NBP table A's midday publish, before `PriceSync`'s 18:30); `appsettings.Development.json` overrides to `0 */3 * * * ?`. Behind `Testing:DisableBackgroundJobs` like the others.
4. **One scheduler.** `AddFxSyncJob` must be called after `AddPriceSyncJob` and adds only `q.AddJob`/`q.AddTrigger` through a second `AddQuartz(...)` — it never repeats `UsePersistentStore`/`UseClustering`, which stay owned by `AddPriceSyncJob` (same contract `AddHistoryBackfillJob` already documents). Persistence, clustering and `DisallowConcurrentExecution` therefore apply to `FxSyncJob` unchanged.
5. **FX run shape.** Currencies = every catalog row except PLN (so GBP/CHF are covered even though no user can pick them). One `IFxRateSource.FetchLatestAsync(codes)` call for the latest rates — NBP table A returns all codes in one request. Before that, any currency with **zero** `FxRate` rows for its `<CODE>PLN` pair is backfilled through `FetchHistoryAsync(code, today-365, today)`; `NbpFxRateSource` already chunks the 93-day API limit. Per-currency backfill failure is isolated (counted failed, run continues). `SyncRun.Kind = Fx`; `DailyPricesSynced { Kind = Fx }` published in the same `SaveChangesAsync` as the run's completion write, on `Completed` or `Partial` only — identical to `PriceSyncJob`.
6. **FX leaves the other two jobs.** `PriceSyncJob` drops its currencies/FX block and its `IFxRateSource` dependency (the M1.4 `SupportedCurrencies` union and its `FailedCount` scoping go with it — `FxSyncJob` covers every catalog currency unconditionally, which is strictly wider). `HistoryBackfillJob` drops `BackfillFxAsync` and `IFxRateSource`: a currency's history is now the FX job's job, not a per-instrument side effect. `FxSyncJob` becomes the only `IFxRateSource` consumer.
7. **`SyncRunKind` (`Prices | Fx | Backfill`)** on `SyncRun`, string-converted, `HasMaxLength(20)`, migrated in with `defaultValue: "Prices"` (every existing row was a price sync); index `(Kind, StartedAt desc)`. `HistoryBackfillJob` now writes one `SyncRun { Kind = Backfill }` per instrument run so all three jobs share one log — it publishes nothing (one instrument's history gives Reporting nothing new to recompute), which is why the contract enum has only two members.
8. **`DailyPricesSynced.Kind` is `PriceSyncKind` (`Prices = 0 | Fx = 1`)**, a separate contract enum from `SyncRunKind`. **Both kinds trigger a Reporting snapshot recompute** — not one per day: the consumer is idempotent per (portfolio, date) through its existing upsert on the `(PortfolioId, Date)` unique index plus inbox dedup by `MessageId`, so a same-day Prices run followed by an Fx run simply overwrites the same rows with fresher inputs. Suppressing the second would leave the day's snapshot valued on whichever job happened to finish first.
9. **`AssetInstrumentLink (AssetId PK, InstrumentId?, Version, IsRemoved)`** is what makes the usage consumers idempotent and order-safe; `InstrumentUsage (InstrumentId PK, AssetCount, FirstUsedAt)` is **derived** from it. `AssetPositionChangedConsumer` (`AssetPositionChanged` from spec-02): ignore if `IsRemoved`, or if a link exists with `Version >= message.Version` (late/duplicate delivery); otherwise upsert the link with `InstrumentId = message.ValuationMode is Market ? message.InstrumentId : null` and recompute `AssetCount` for the old and the new instrument as `COUNT(links WHERE InstrumentId = X AND NOT IsRemoved)`. Recomputing instead of ±1 arithmetic is what makes an instrument switch on update, a redelivery and an out-of-order event all converge on the same number.
10. **`AssetRemovedConsumer`** (`AssetRemoved` from spec-02) sets `IsRemoved = true`, clears `InstrumentId` and recomputes the old instrument's count. Removal is terminal — the tombstone row stays, so a redelivered `AssetRemoved` and any later `AssetPositionChanged` for that asset are both no-ops. `AssetRemoved` carries no `Version`, which is exactly why the terminal flag, not a version compare, guards it.
11. **`PortfolioIsArchived` is ignored** by the usage consumers: an archived portfolio's assets still count as in use, so prices stay current for a restore. The flag also flips through `Portfolio*` events this service does not consume, so keying usage on it would go stale.
12. **Backfill on first use.** When a recompute takes an instrument's `AssetCount` from 0 to 1, the consumer calls `IHistoryBackfillTrigger.EnqueueAsync` — **after** `SaveChangesAsync`, since it schedules a Quartz job outside the consume transaction; a duplicate enqueue is harmless because backfill upserts by (instrument, date). `FirstUsedAt` is stamped once, when the usage row is created, and never rewritten; re-attaching after a full detach re-enqueues (a 0 → 1 transition) but does not move `FirstUsedAt`. Consumers depending on `IHistoryBackfillTrigger` (namespace `Sources`) is fine — `ArchitectureTests.OnlySourcesNamespace_DependsOn_PriceSourceAbstractions` guards `IPriceSource`/`IFxRateSource` only.
13. **`PriceSyncJob.GetInstrumentsToSyncAsync`** joins `InstrumentUsage` and keeps `AssetCount > 0`. **No exception for `Unverified` instruments** — their status is resolved by `HistoryBackfillJob`, which already runs on creation (`AddCustomInstrument`, unchanged) and now on first use, so nothing depends on the daily job for them. The skipped count (`total − selected`) goes into the run's log line and an activity tag `skarbiec.sync_run.skipped`; no new `SyncRun` column, so the stored counters keep meaning "attempted".
14. **`SyncStatusResponse` is reshaped, not kept compatible** (ADR-019): `{ HasRun, Prices?, Fx?, Backfill? }` over a `SyncRunSummary { RunId, Status, StartedAt, FinishedAt, SyncedCount, NoDataCount, FailedCount }`. `HasRun` keeps its meaning (any run of any kind), so the "no sync has run yet" branch survives. The Angular settings page and `settings.spec.ts` are updated in the same change and the client regenerated; the payload carries `Backfill` for shape uniformity but the UI renders only Prices and Fx.
15. MarketData's DbContext already registers inbox/outbox entities with `MarketData` table prefixes — verified, no change needed there beyond the new entities.

## Data / API changes

| Entity / endpoint | Change | Migration? |
|---|---|---|
| `Currency` | new: `Code` PK char(3), `Name`, `Symbol`, `DecimalPlaces` (default 2), `DisplayOrder` | yes |
| `InstrumentUsage` | new: `InstrumentId` PK, `AssetCount`, `FirstUsedAt` | yes |
| `AssetInstrumentLink` | new: `AssetId` PK, `InstrumentId?`, `Version`, `IsRemoved` | yes |
| `SyncRun` | + `Kind` (string, default `Prices`), index `(Kind, StartedAt desc)` | yes |
| `FxRate` | seeder's four 2020-01-01 bootstrap rows removed (data only) | no |
| `DailyPricesSynced` | + `Kind: PriceSyncKind` (`Prices \| Fx`), edited in place | n/a |
| `GET /api/marketdata/sync/status` | response reshaped to per-kind summaries | n/a |

## Acceptance criteria

1. Given a fresh database, when `MarketDataSeeder.SeedAsync` runs, then a `Currency` row exists for every code in `SupportedCurrencies.All` plus GBP and CHF, and no `FxRate` row is written. — proof: `MarketDataSeederTests.SeedAsync_SeedsEveryCurrencyInSupportedCurrencies`, `MarketDataSeederTests.SeedAsync_WritesNoBootstrapFxRates`
2. Given the seeder already ran, when it runs again, then instrument and currency counts are unchanged. — proof: `MarketDataSeederTests.SeedAsync_CalledTwice_DoesNotDuplicateInstrumentsOrCurrencies`
3. Given a catalog with PLN/EUR/USD/GBP/CHF and existing FX history, when `FxSyncJob.RunAsync` runs, then a rate row for today exists for every non-PLN pair and none for `PLNPLN`. — proof: `FxSyncJobTests.RunAsync_UpsertsLatestRateForEveryCatalogCurrencyExceptPln`
4. Given a currency with no `FxRate` row at all, when the job runs, then `IFxRateSource.FetchHistoryAsync` is called once for it over a ≥365-day range and the rates are stored. — proof: `FxSyncJobTests.RunAsync_FirstRunForCurrency_BackfillsTwelveMonths`
5. Given that currency now has history, when the job runs again, then no further history fetch happens for it. — proof: `FxSyncJobTests.RunAsync_SecondRun_DoesNotBackfillAgain`
6. Given the source returns `NoData` (weekend/holiday), when the job runs, then the run records `NoDataCount` and its status is not `Failed`. — proof: `FxSyncJobTests.RunAsync_NoDataDay_RecordsNoDataWithoutFailing`
7. Given one currency's backfill errors, when the job runs, then the other currencies are still synced and the run is `Partial`. — proof: `FxSyncJobTests.RunAsync_OneCurrencyBackfillFails_OthersStillSynced_RunRecordedAsPartial`
8. Given a completed FX run, when it finishes, then a `SyncRun` with `Kind = Fx` exists. — proof: `FxSyncJobTests.RunAsync_WritesSyncRunWithKindFx`
9. Given a successful FX run on a hostless provider, when it finishes, then the `DailyPricesSynced` outbox row (`Kind = Fx`) and the `SyncRun` completion write are committed in the same transaction. — proof: `MarketDataOutboxTests.RunAsync_FxRun_WritesDailyPricesSyncedFxOutboxMessageInSameTransactionAsSyncRunRow`
10. Given a shortened dev cron, when the host starts, then `FxSyncJob` fires by itself and emits an `FxSyncJob.Run` span. — proof: `FxSyncSchedulingTests.AddFxSyncJob_OnShortenedDevCron_FiresAutomatically_WithTraceSpan`
11. Given no usage row for an instrument, when an `AssetPositionChanged` referencing it arrives, then `InstrumentUsage.AssetCount` is 1, `FirstUsedAt` is set, and `IHistoryBackfillTrigger.EnqueueAsync` is called exactly once. — proof: `InstrumentUsageConsumerTests.Consume_FirstAssetForInstrument_SetsUsageToOne_AndEnqueuesBackfillOnce`
12. Given usage 1, when a second asset points at the same instrument, then the count is 2 and no second backfill is enqueued. — proof: `InstrumentUsageConsumerTests.Consume_SecondAssetForSameInstrument_UsageTwo_NoSecondBackfill`
13. Given usage 2, when `AssetRemoved` arrives for one of the assets, then the count is 1. — proof: `InstrumentUsageConsumerTests.Consume_AssetRemoved_DecrementsUsage`
14. Given an `AssetPositionChanged` already consumed, when the same `MessageId` is delivered again, then the count is unchanged. — proof: `InstrumentUsageConsumerTests.Consume_SameMessageIdDeliveredTwice_UsageCountedOnce`
15. Given version 5 was applied, when version 3 for the same asset arrives, then nothing changes. — proof: `InstrumentUsageConsumerTests.Consume_OutOfOrderVersion_Ignored`
16. Given an asset pointing at instrument A, when an update repoints it at B, then A's count drops by one and B's rises by one. — proof: `InstrumentUsageConsumerTests.Consume_InstrumentSwitchedOnUpdate_MovesTheCount`
17. Given two instruments where only one has `AssetCount > 0`, when `PriceSyncJob.RunAsync` runs, then only the used one is fetched and the skipped count is logged. — proof: `PriceSyncJobTests.RunAsync_SyncsOnlyInstrumentsInUse`
18. Given a Prices run and a later Fx run, when `GET /api/marketdata/sync/status` is called, then the response carries the latest run for each kind separately. — proof: `GetSyncStatusEndpointTests.Get_AfterPricesAndFxRuns_ReturnsLatestRunPerKind`
19. Given a USD instrument, when `HistoryBackfillJob.RunAsync` runs, then a year of quotes is stored, no `FxRate` row is written, and a `SyncRun { Kind = Backfill }` exists. — proof: `HistoryBackfillJobTests.RunAsync_UsdInstrument_BackfillsOneYearOfQuotes_AndWritesNoFxRates`, `HistoryBackfillJobTests.RunAsync_WritesSyncRunWithKindBackfill`
20. Given a `DailyPricesSynced` payload with `Kind: "Fx"` and an unknown extra field, when it is deserialized, then `Kind` is `Fx` and the known fields are intact. — proof: `DailyPricesSyncedContractTests.Deserialize_FxKindPayload_MapsToFx`
21. Given a `DailyPricesSynced { Kind = Fx }`, when Reporting consumes it, then snapshots are recomputed exactly as for `Prices`. — proof: `DailyPricesSyncedConsumerTests.Consume_FxKind_RecomputesSnapshots`
22. Given the assembly, when the architecture guardrail runs, then only `Sources` types depend on `IPriceSource`/`IFxRateSource`. — proof: `ArchitectureTests.OnlySourcesNamespace_DependsOn_PriceSourceAbstractions`
23. Given the settings page, when the status resource resolves, then the last Prices run and the last FX run are both rendered. — proof: `web/src/app/features/settings/settings.spec.ts` — "shows the last Prices run and the last FX run"

## Verification

- `node scripts/verify.mjs --projects MarketData,Reporting,Contracts --web --api` (`--web --api` because `SyncStatusResponse` changes the generated TS client and the settings component).
- After `dotnet ef migrations add CurrenciesAndInstrumentUsage`: run `dotnet format`, and confirm the generated migration touches no `xmin` shadow property.
- Manual smoke under `dotnet run --project Skarbiec.AppHost` (local DBs reset first — ADR-019): add a market asset in the UI, then within the dev cron window confirm in `marketdata_db` that a `SyncRun { Kind = Backfill }` (from first use) and a `SyncRun { Kind = Prices }` appear, that the Prices run covers only the attached instrument, and that Settings shows both a Prices and an FX status.

## Risks / open questions

<!-- empty -->

## Result

<!-- filled in by ops after Ship -->
