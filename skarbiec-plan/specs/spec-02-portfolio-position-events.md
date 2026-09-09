---
title: Portfolio position events
status: draft
tier: 2
branch: feat/portfolio-position-events
created: 2026-09-06
---

## Goal

Every mutation of a portfolio, asset or transaction leaves Portfolio as an event carrying the full position state, so a consumer never has to call Portfolio back to learn what changed (ADR-021).

## Why

Closes three rows of `architecture.md` §"Current vs target":

- `AssetChanged`/`TransactionRecorded` carry ids and a change kind but no state — a consumer would have to re-query Portfolio for quantity, mode, currency. Neither event has a consumer today, so replacing them costs nothing (`architecture.md` §Events).
- `UpdateTransaction` and `DeleteTransaction` publish **nothing at all** — a real behavioral gap: quantity is the source of truth (ADR-009) and it can change with no event.
- `Portfolio.AssetCount` / `Asset.TransactionCount` are denormalized counters kept in sync by hand in five handlers, guarding two deletes that a `AnyAsync` answers directly (`domain.md` §Portfolio).

spec-03 (Reporting's `Position` read model) and spec-04 (MarketData's `InstrumentUsage`) both consume what this spec publishes.

## Scope

### Backend

**`contracts/Skarbiec.Contracts/Events/`** — new records, all `sealed record`, all with `OccurredAtUtc` (`DateTimeOffset`):

- `AssetPositionChanged { AssetId, PortfolioId, UserId, AssetClass, ValuationMode, InstrumentId?, Currency, Quantity, ManualValueAmount?, ManualValueDate?, PortfolioIsArchived, Version (long), OccurredAtUtc }`
- `AssetRemoved { AssetId, PortfolioId, UserId, OccurredAtUtc }`
- `PortfolioArchived` / `PortfolioRestored` / `PortfolioDeleted`, each `{ PortfolioId, UserId, OccurredAtUtc }`

Deleted in the same change (ADR-019, edit in place): `AssetChanged.cs`, `TransactionRecorded.cs`, `AssetChangedContractTests.cs`, `TransactionRecordedContractTests.cs` and their JSON fixtures. Two stale comments naming them are updated: `Skarbiec.AppHost/AppHost.cs:43` and `services/MarketData/.../Sources/PriceSyncJob.cs:20`.

**`services/Portfolio/Skarbiec.Portfolio/`**

- `Data/Asset.cs`: `+ long Version` (see Design decisions), `− int TransactionCount`. `Data/Portfolio.cs`: `− int AssetCount`. `Data/PortfolioDbContext.cs`: the `xmin` shadow token is renamed `"Version"` → `"Xmin"` on both `Asset` and `Transaction` (column name `xmin` unchanged) to free the name for the new counter.
- `Features/PositionEventPublisher.cs` (new, scoped, `PortfolioDbContext` + `IPublishEndpoint` + `TimeProvider`): the single place that increments `Asset.Version`, resolves `PortfolioIsArchived` and publishes `AssetPositionChanged` — called before `SaveChangesAsync` by all five mutating slices, plus a `PublishForEveryAssetAsync(portfolio)` overload for the archive/restore fan-out. Five call sites, so extraction is the rule, not a shortcut (ADR-002).
- Publish `AssetPositionChanged` from `AddAsset`, `UpdateAsset`, `RecordTransaction`, `UpdateTransaction`, `DeleteTransaction`; `AssetRemoved` from `RemoveAsset`; `PortfolioArchived` from `ArchivePortfolio`; `PortfolioDeleted` from `DeletePortfolio`.
- New slice `RestorePortfolio` (`POST /api/portfolio/portfolios/{id}/restore`, `Ok<PortfolioResponse>`), mirroring `ArchivePortfolio`: clears `IsArchived`, publishes `PortfolioRestored`.
- Archive **and** restore also publish one `AssetPositionChanged` per asset of that portfolio, carrying the new `PortfolioIsArchived` value — otherwise a read model would keep valuing an archived portfolio.
- Delete guards: `Assets.AnyAsync(a => a.PortfolioId == id)` in `DeletePortfolioHandler`, `Transactions.AnyAsync(t => t.AssetId == id)` in `RemoveAssetHandler`. Every `AssetCount++`/`TransactionCount--` line disappears, and with them the two tests' private `BumpAssetCountAsync`/`BumpTransactionCountAsync` seeders — the guards are now provable through the API alone.
- `PortfolioResponse.AssetCount` / `AssetResponse.TransactionCount` keep their names and types; `ToResponse(...)` takes the count as a parameter and every slice returning one supplies it: list/get project it as a correlated subquery inside the same query (no N+1, no second round trip), while `CreatePortfolio` passes `0` and `AddAsset` `0`/`1` — they already know.
- `Program.cs`: register `PositionEventPublisher`, `RestorePortfolioHandler`, `TimeProvider.System` (`TryAddSingleton`, mirroring Reporting), map the restore endpoint.
- `requests/portfolio.http`: add the restore call.

### Frontend

`npm run gen:api` (new `postApiPortfolioPortfoliosByIdRestore`, unchanged response shapes). In `web/src/app/features/portfolios/portfolios.ts`/`.html`: a "Restore" menu item rendered only when `portfolio.isArchived`, mirroring `archive()` — `ConfirmDialog`, snackbar on failure, `portfoliosResource.reload()` on success. The Archive dialog copy loses its "Archiving can't be undone from here yet" sentence. Nothing else in the UI changes.

## Out of scope

- `Features/GetPositionsForValuation/*` and its `SystemCaller` policy — **spec-03 deletes them**; here they keep compiling and passing unchanged.
- Reporting and MarketData consumers of the new events (spec-03, spec-04). Nothing consumes `AssetChanged`/`TransactionRecorded` today, so removing them breaks no consumer.
- Squashing Portfolio's migration history to one `InitialCreate` (spec-06).
- Any change to quantity math (`TransactionQuantityCalculator`), to the three valuation modes, or to Angular beyond the restore action.

## Design decisions

1. **`Version` is an explicit `long` counter on `Asset`, not `xmin`.** `xmin` is a `uint` transaction id that wraps, and — decisive here — it does **not** change when the asset row itself isn't written, which is exactly the archive/restore fan-out case (only `Portfolio.IsArchived` changes). A counter the publisher bumps on every published mutation is monotonic per asset by construction and survives the fan-out. Cost: the shadow concurrency token is renamed to `"Xmin"`; the generated migration must not touch the `xmin` column (`.claude/rules/dotnet.md` — delete those lines by hand if the scaffolder emits them).
2. **Archive/restore are no-ops when the portfolio is already in the target state** — 200 with the unchanged body, and **no** event: an event is a fact that happened (`.claude/rules/messaging.md`), and a fan-out of N position events on every repeated click is pure noise.
3. **`AssetRemoved` carries no `Version`.** It is terminal for that `AssetId`: Portfolio never mutates an asset after deleting it, and a re-created asset gets a fresh `Guid`. The inbox's `MessageId` dedup covers redelivery; a tombstone table would buy nothing.
4. **The counters stay in the API, not in the database.** The Angular menu hides Delete on `assetCount === 0` / `transactionCount === 0`; keeping the field names means no UI change and no regenerated-client churn beyond the new route.
5. `OccurredAtUtc` comes from an injected `TimeProvider`, so an outbox test can assert a pinned timestamp instead of `DateTimeOffset.UtcNow`.
6. `domain.md` §Portfolio gains `Version` in the `Asset` row in this PR (Definition of Done: docs the spec touched move with it).

## Data / API changes

| Entity / endpoint | Change | Migration? |
|---|---|---|
| `Asset` | `+ Version` (bigint, default 0); `− TransactionCount`; xmin shadow token renamed to `Xmin` (column unchanged) | `PositionEvents` |
| `Portfolio` | `− AssetCount` | `PositionEvents` |
| `POST /api/portfolio/portfolios/{id}/restore` | new endpoint, `200 PortfolioResponse` / `404` | — |
| `PortfolioResponse.AssetCount`, `AssetResponse.TransactionCount` | same shape, now computed by subquery | — |
| `Skarbiec.Contracts.Events` | `+ AssetPositionChanged, AssetRemoved, PortfolioArchived, PortfolioRestored, PortfolioDeleted`; `− AssetChanged, TransactionRecorded` | — |

## Acceptance criteria

1. Given an owned portfolio, when an asset is added, then an `AssetPositionChanged` row carrying the asset's full state commits in the same transaction as the asset row. — proof: `PortfolioOutboxTests.AddAsset_WritesAssetPositionChangedInSameTransactionAsAssetRow`
2. Given an existing asset, when it is updated (including a valuation-mode switch), then one `AssetPositionChanged` carries the post-update `ValuationMode`, `InstrumentId`, `Currency` and `ManualValue*`. — proof: `PortfolioOutboxTests.UpdateAsset_WritesAssetPositionChangedWithNewValuationMode`
3. Given an asset with history, when a transaction is recorded, then the published `Quantity` equals the recomputed quantity. — proof: `PortfolioOutboxTests.RecordTransaction_WritesAssetPositionChangedWithRecomputedQuantity`
4. Given an asset with two transactions, when one is **edited**, then an `AssetPositionChanged` is published carrying the recomputed quantity (today: nothing is published). — proof: `PortfolioOutboxTests.UpdateTransaction_WritesAssetPositionChangedWithRecomputedQuantity`
5. Given an asset with two transactions, when one is **deleted**, then an `AssetPositionChanged` is published carrying the recomputed quantity (today: nothing is published). — proof: `PortfolioOutboxTests.DeleteTransaction_WritesAssetPositionChangedWithRecomputedQuantity`
6. Given an asset with no transactions, when it is removed, then `AssetRemoved` commits in the same transaction as the deletion. — proof: `PortfolioOutboxTests.RemoveAsset_WritesAssetRemovedInSameTransactionAsDeletion`
7. Given the same asset mutated twice, when both events are read, then the second `Version` is strictly greater than the first. — proof: `PortfolioOutboxTests.SuccessiveMutations_PublishStrictlyIncreasingVersionPerAsset`
8. Given a portfolio holding two assets, when it is archived, then one `PortfolioArchived` **and** two `AssetPositionChanged` with `PortfolioIsArchived = true` are written in that one transaction. — proof: `PortfolioOutboxTests.ArchivePortfolio_WritesPortfolioArchivedAndOnePositionEventPerAsset`
9. Given an archived portfolio holding two assets, when `POST /portfolios/{id}/restore` is called, then it returns 200 with `isArchived: false` and writes `PortfolioRestored` plus two `AssetPositionChanged` with `PortfolioIsArchived = false`. — proof: `RestorePortfolioEndpointTests.Restore_ArchivedPortfolio_ReturnsOkAndClearsArchivedFlag` + `PortfolioOutboxTests.RestorePortfolio_WritesPortfolioRestoredAndOnePositionEventPerAsset`
10. Given a portfolio that is not archived, when restore is called, then it returns 200 and publishes nothing. — proof: `RestorePortfolioEndpointTests.Restore_NotArchivedPortfolio_ReturnsOkAndPublishesNothing`
11. Given a stranger's token, when restore is called on another user's portfolio, then 404 (never 403). — proof: `RestorePortfolioEndpointTests.Restore_ByStranger_ReturnsNotFound`
12. Given an empty portfolio, when it is deleted, then `PortfolioDeleted` commits with the deletion. — proof: `PortfolioOutboxTests.DeletePortfolio_WritesPortfolioDeletedInSameTransactionAsDeletion`
13. Given a portfolio holding a real asset (created over HTTP, no counter to seed), when delete is called, then 409 pointing at archive; and given an asset with a real transaction, remove returns 409. — proof: `DeletePortfolioEndpointTests.Delete_PortfolioContainingAssets_ReturnsConflictPointingToArchive`, `RemoveAssetEndpointTests.Remove_AssetWithTransactions_ReturnsConflict`
14. Given two assets under a portfolio and two transactions under an asset, when the list/get endpoints are called, then `assetCount` = 2 and `transactionCount` = 2 with no counter column in the database. — proof: `ListPortfoliosEndpointTests.List_PortfolioWithTwoAssets_ReportsAssetCountFromAssets`, `GetAssetEndpointTests.Get_AssetWithTransactions_ReportsTransactionCountFromTransactions`, `ArchitectureTests.PortfolioEntities_ExposeNoDenormalizedCounters`
15. Given each new contract, when a payload with an unknown extra field is deserialized, then the known fields still bind. — proof: `AssetPositionChangedContractTests.Deserialize_FixtureWithUnknownFields_StillDeserializesKnownFields`, `AssetRemovedContractTests.Deserialize_FixtureWithUnknownFields_StillDeserializesKnownFields`, `PortfolioLifecycleContractTests` (one fact per `Portfolio*` record)
16. Given the merged branch, when the repository is searched, then no `AssetChanged`/`TransactionRecorded` type, publish site, test or comment survives. — proof: `git grep -n "AssetChanged\|TransactionRecorded" -- contracts services web Skarbiec.AppHost` returns nothing
17. Given an archived portfolio in the list, when the row menu is opened, then "Restore" is offered, confirmed through `ConfirmDialog`, and the list reloads after a successful call. — proof: `web/src/app/features/portfolios/portfolios.spec.ts` → `'shows Restore only for archived portfolios'`, `'restores an archived portfolio after confirmation and reloads'`, `'does not restore when the confirmation is cancelled'`

## Verification

```
node scripts/verify.mjs --projects Portfolio,Contracts --web --api
```

Manual smoke (Docker + `dotnet run --project Skarbiec.AppHost`): create a portfolio, add an asset, archive it from the row menu, reload with "Show archived" on, use Restore — the chip disappears and the row returns to the default list. In the Aspire dashboard, the RabbitMQ/outbox trace for the archive shows `PortfolioArchived` plus one `AssetPositionChanged` per asset.

## Risks / open questions

- The archive/restore fan-out writes one `UPDATE` per asset (version bump) plus one outbox row per asset in a single transaction. Fine at personal-portfolio scale (tens of assets); if a portfolio ever holds thousands, this becomes a batch job instead — revisit only if that happens.

## Result
