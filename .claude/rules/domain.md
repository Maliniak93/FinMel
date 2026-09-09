---
paths:
  - "services/**"
  - "contracts/**"
  - "gateway/**"
  - "web/**"
---

# Skarbiec domain model & invariants

Target model. Items marked *(target — spec-0x)* are not implemented yet; do not assume they exist in code, and do not build around their absence either — check `skarbiec-plan/domain.md` and the owning spec first.

## Assets and transactions

- `AssetClass` (`Skarbiec.Contracts`): Cash, Deposit, Stock, Etf, Bond, Crypto, PreciousMetal, RealEstate, Other.
- `TransactionType`: Buy, Sell, Deposit, Withdraw, Dividend, Interest, Fee. **Buy/Deposit increase quantity, Sell/Withdraw decrease it**; Dividend, Interest and Fee do not change quantity. Quantity is derived from transactions, never stored as an independent truth (ADR-009).
- Manual-valuation assets may exist with no transactions at all.

## Valuation modes

`ValuationMode` is explicit on the asset, defaulted per class by `AssetValuationModes` in `Skarbiec.Contracts`:

| Mode | Default for | Value |
| --- | --- | --- |
| `Market` | Stock, Etf, Bond, Crypto, PreciousMetal | `Quantity × last PriceQuote(instrument, ≤ date) × FxRate(quoteCurrency→PLN, ≤ date)` |
| `CurrencyValued` | Cash, Deposit | `Quantity × FxRate(assetCurrency→PLN, ≤ date)` — no instrument, no manual value |
| `Manual` | RealEstate, Other | `ManualValueAmount × FxRate(assetCurrency→PLN, ≤ date)`, as of `ManualValueDate` |

The default is a starting point the user may override; the mode, not the class, decides how a value is computed. An asset has exactly one mode, and `Market` requires an `InstrumentId`.

## Valuation algorithm

Use the **last known** price and FX rate at or before the snapshot date (weekends, holidays, gaps). Mark the line `IsStale` when the price or rate used is more than 7 days older than the snapshot date. Missing data never zeroes a position — it produces a stale line.

## Currencies

- User-chosen currencies (`Portfolio.Currency`, `Asset.Currency`) come from `SupportedCurrencies` in `Skarbiec.Contracts` — PLN, EUR, USD; default PLN; matched uppercase and exactly. Frontend mirror: `web/src/app/shared/currencies.ts`.
- MarketData keeps its **own** currency catalog *(target — spec-04)*: `Currency (Code PK, Name, Symbol, DecimalPlaces, DisplayOrder)`, seeded PLN, EUR, USD, GBP, CHF, driving `FxSyncJob`. It is deliberately wider than `SupportedCurrencies` — `Instrument.QuoteCurrency` and `FxRate.Pair` (e.g. `USDPLN`) are not restricted to the user-facing set. PLN is the fixed base (ADR-008); there is no pivot flag and no hardcoded fallback rate — a missing history is backfilled by the job.

## Entities per service

**Identity** — `User` (`DisplayName`, no `BaseCurrency` — PLN-only, ADR-008), `RefreshToken`.

**Portfolio** — `Portfolio (Id, UserId, Name, Description?, Currency, IsArchived)`; `Asset (Id, UserId, PortfolioId, AssetClass, ValuationMode, Name, Currency, Quantity, ManualValueAmount?, ManualValueDate?, InstrumentId?, xmin)`; `Transaction (Id, UserId, AssetId, Type, Quantity, UnitPriceAmount, FeeAmount, Date, xmin)`. Delete guards query the children (`Assets.AnyAsync`, `Transactions.AnyAsync`) — no denormalized counters.

**MarketData** — `Instrument (Id, Ticker, Name, Source, QuoteCurrency, AssetClass, VerificationStatus)`, where `InstrumentVerificationStatus` is `Verified | Unverified | Failed` and records the ADR-018 ticker check made once at creation; it is never re-checked from a valuation path, and `Verified` must stay the 0 member (it is the column's `HasDefaultValue`). `PriceQuote`; `FxRate`; `SyncRun (+ Kind: Prices | Fx | Backfill)`; `Currency` *(target — spec-04)*; `InstrumentUsage (InstrumentId PK, AssetCount, FirstUsedAt)` *(target — spec-04)*, maintained from `AssetPositionChanged`/`AssetRemoved` so `PriceSyncJob` syncs only instruments actually in use and first use triggers a history backfill.

**Reporting** — `Position` *(target — spec-03)*, a local read model upserted from `AssetPositionChanged` (the event payload plus `UpdatedAt`, unique on `AssetId`, archived flag from the `Portfolio*` events); `AssetValuation (Id, UserId, PortfolioId, AssetId, Date, AssetClass, Quantity, PriceUsed?, PriceDate?, FxRateUsed?, ValuePln, IsStale)` *(target — spec-03)*, one line per asset per date and the basis for P/L, goals and TWR; `ValuationSnapshot (Id, UserId, PortfolioId, Date, TotalPln, IsStale)` — the per-class breakdown is a `GROUP BY AssetClass` over the lines, not a stored JSON blob. Insights configuration *(target)*: `TargetAllocation` + `TargetAllocationLine` (scope `PortfolioId?`, null = across all portfolios), `EmergencyFund` + `EmergencyFundAsset`, `SavingsGoal` + `SavingsGoalAsset`. Insight math (`AllocationMath`, `RebalancingMath`, `ContributionPlanMath`, `EmergencyFundMath`, `GoalMath`) is pure functions over `AssetValuation` — nothing is recomputed from other services on request.

## Invariants (validate and test)

- Target-allocation lines sum to 100; each line carries its own tolerance band.
- A Sell or Withdraw may never take an asset's quantity below 0 — **at any point in its transaction history**, not just at the end (editing or deleting an older transaction must be re-checked against the whole timeline).
- Amounts, quantities and fees are ≥ 0.
- Unique: one `PriceQuote` per (instrument, date), one `FxRate` per (pair, date), one `AssetValuation` per (asset, date).
- Every user-owned entity has `UserId` — enforced by a NetArchTest architecture test.
- Cross-service references are plain `Guid` columns, no FK (`Asset.InstrumentId` → MarketData; `Position.AssetId` → Portfolio).

## Data ownership

| Service | Owns |
| --- | --- |
| Identity | User, RefreshToken |
| Portfolio | Portfolio, Asset, Transaction |
| MarketData | Currency, Instrument, PriceQuote, FxRate, SyncRun, InstrumentUsage |
| Reporting | Position, AssetValuation, ValuationSnapshot, TargetAllocation(+Line), EmergencyFund(+Asset), SavingsGoal(+Asset) |

## Conscious simplifications

Transactions are single-entry: a Dividend or Interest on one asset creates no matching cash inflow anywhere, and a fee is expressed in the asset's own currency (there is no separate fee currency). The base currency is PLN only — the user picks a currency per portfolio and asset, but every valuation is reported in PLN.

## Misc

- Allocation, rebalancing, contribution-plan and goal output is **information, never investment advice** — the disclaimer travels with it into the UI.
- Secrets: user-secrets locally, `.env` on the VPS — never in the repo.
- Code style lives in `dotnet.md` and `angular.md`; events in `messaging.md`. This file is domain only.
