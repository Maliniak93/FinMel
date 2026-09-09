# Domain model

Target state (Część II.3). Items that don't exist on `master` yet are marked **(target — spec-0x)**; everything else is already true today. Cross-service references are marked "ref, no FK" — see the rule at the bottom.

## Asset classes and valuation modes

`AssetClass`: `Cash`, `Deposit`, `Stock`, `Etf`, `Bond`, `Crypto`, `PreciousMetal`, `RealEstate`, `Other`.

`AssetValuationMode` — three modes, explicit on `Asset.ValuationMode` (M1.4; before that, market-vs-manual was inferred from `InstrumentId is not null`, which had no way to express the third mode):

| Mode | Shape | Default classes |
|---|---|---|
| **Market** | points at an `Instrument` (ticker + source); value = quantity × last price × FX rate | Stock, Etf, Bond, Crypto, PreciousMetal |
| **Manual** | `ManualValueAmount` + `ManualValueDate`, refreshed by hand | RealEstate, Other |
| **Currency-valued** | no instrument, no manual value; value = quantity × FX rate for the asset's own currency (rate = 1 for PLN, so no lookup at all) | Cash, Deposit |

The class → default-mode mapping is a default, not a hard constraint — Market and Manual stay available to every class; only "neither instrument nor manual value" is gated by class, since that combination was always a validation error before Currency-valued existed.

## Identity (`identity_db`)

| Entity | Fields | Notes |
|---|---|---|
| `User` | Identity + `DisplayName` | `BaseCurrency` removed **(target — spec-05)** — unused, PLN is the only base currency (ADR-008) |
| `RefreshToken` | token hash, expiry, revoked | unchanged |

## Portfolio (`portfolio_db`)

| Entity | Fields | Changes vs today |
|---|---|---|
| `Portfolio` | `Id, UserId, Name, Description?, Currency, IsArchived` | `AssetCount` removed **(target — spec-02)** — delete guard becomes `Assets.AnyAsync` |
| `Asset` | `Id, UserId, PortfolioId, AssetClass, ValuationMode, Name, Currency, Quantity, ManualValueAmount?, ManualValueDate?, InstrumentId?, xmin` | `ValuationMode` already explicit (M1.4); `TransactionCount` removed **(target — spec-02)** — delete guard becomes `Transactions.AnyAsync` |
| `Transaction` | `Id, UserId, AssetId, Type, Quantity, UnitPriceAmount, FeeAmount, Date, xmin` | unchanged |

Every slice that mutates a position publishes `AssetPositionChanged` in the same transaction as the write **(target — spec-02: today only add/update-asset do; transaction update/delete publish nothing)**.

```mermaid
erDiagram
    PORTFOLIO ||--o{ ASSET : contains
    ASSET ||--o{ TRANSACTION : records
    ASSET }o--o| INSTRUMENT : "valued by (ref, no FK)"
    PORTFOLIO {
        uuid id PK
        uuid user_id
        string name
        string currency
        bool is_archived
    }
    ASSET {
        uuid id PK
        uuid user_id
        uuid portfolio_id FK
        string asset_class
        string valuation_mode "Market Manual CurrencyValued"
        string currency
        numeric quantity
        numeric manual_value_amount "Manual only"
        date manual_value_date "Manual only"
        uuid instrument_id "Market only"
        xid xmin "concurrency token"
    }
    TRANSACTION {
        uuid id PK
        uuid user_id
        uuid asset_id FK
        string type "Buy Sell Deposit Withdraw Dividend Interest Fee"
        numeric quantity
        numeric unit_price_amount
        numeric fee_amount
        date date
        xid xmin "concurrency token"
    }
```

## MarketData (`marketdata_db`)

| Entity | Fields | Role |
|---|---|---|
| `Currency` | `Code (PK), Name, Symbol, DecimalPlaces, DisplayOrder` | **(target — spec-04, new)** no `FallbackRateToPln`, no `IsPivot` (PLN is the fixed base). Seed: PLN, EUR, USD, GBP, CHF |
| `Instrument` | `Id, Ticker, Name, Source, QuoteCurrency, AssetClass, VerificationStatus` | unchanged; `QuoteCurrency` is unconstrained (whatever the provider quotes) |
| `PriceQuote` | `InstrumentId, Date, ClosePrice` — unique (instrument, date) | unchanged |
| `FxRate` | `Pair (e.g. USDPLN), Date, Rate` — unique (pair, date) | unchanged |
| `SyncRun` | + `Kind: Prices \| Fx \| Backfill` | **(target — spec-04)** one log shape for all three jobs |
| `InstrumentUsage` | `InstrumentId (PK), AssetCount, FirstUsedAt` | **(target — spec-04, new)** built from the `AssetPositionChanged`/`AssetRemoved` consumer; `PriceSyncJob` syncs only `AssetCount > 0` |

Jobs: `FxSyncJob` **(target — spec-04)** daily over every catalog currency, 12-month backfill on first run; `PriceSyncJob` today syncs every dictionary instrument, target-only instruments in use; `HistoryBackfillJob` today fires for a newly created custom instrument, target also fires from the `InstrumentUsage` consumer on first use of an existing one. All three respect `Testing:DisableBackgroundJobs`.

```mermaid
erDiagram
    INSTRUMENT ||--o{ PRICE_QUOTE : "has quotes"
    INSTRUMENT ||--o| INSTRUMENT_USAGE : "tracked by"
    CURRENCY ||--o{ FX_RATE : "quoted via pair"
    CURRENCY {
        string code PK "target - spec-04"
        string name
        string symbol
        int decimal_places
    }
    INSTRUMENT {
        uuid id PK
        string ticker
        string source "Nbp Stooq CoinGecko"
        string quote_currency
        string asset_class
        string verification_status "Verified Unverified Failed"
    }
    PRICE_QUOTE {
        uuid id PK
        uuid instrument_id FK
        date quote_date UK
        numeric close_price
    }
    FX_RATE {
        uuid id PK
        string pair UK "e.g. USDPLN"
        date rate_date UK
        numeric rate
    }
    INSTRUMENT_USAGE {
        uuid instrument_id PK "target - spec-04"
        int asset_count
        datetime first_used_at
    }
    SYNC_RUN {
        uuid id PK
        string kind "Prices Fx Backfill; Kind field is target - spec-04"
        datetime started_at
        bool success
    }
```

## Reporting (`reporting_db`)

| Entity | Fields | Role |
|---|---|---|
| `Position` | copy of the `AssetPositionChanged` payload + `UpdatedAt`; unique `AssetId` | **(target — spec-03, new)** upserted from the inbox; `IsArchived` kept current from `Portfolio*` events |
| `ValuationSnapshot` | `Id, UserId, PortfolioId, Date, TotalPln, IsStale` | `BreakdownJson` removed **(target — spec-03)** — breakdown becomes `GROUP BY AssetClass` over `AssetValuation` lines |
| `AssetValuation` | `Id, UserId, PortfolioId, AssetId, Date, AssetClass, Quantity, PriceUsed?, PriceDate?, FxRateUsed?, ValuePln, IsStale`; unique `(AssetId, Date)` | **(target — spec-03, new)** the basis for P/L per asset, emergency fund and goal math, and TWR — nothing needs recomputing from scratch |
| `TargetAllocation` + `TargetAllocationLine` | scope (`PortfolioId?`, null = total), lines `AssetClass → TargetPercent, ToleranceBand`; unique `(UserId, PortfolioId?)` | **(target — insights spec, after spec-03)** |
| `EmergencyFund` + `EmergencyFundAsset` | `MonthlyExpenses, TargetMonths` + designated assets, validated locally against `Position` | **(target — insights spec)** |
| `SavingsGoal` + `SavingsGoalAsset` | `Name, TargetAmount, Deadline, AnnualReturnRate` + linked assets | **(target — insights spec)** |

Insight math (`AllocationMath`, `RebalancingMath`, `ContributionPlanMath`, `EmergencyFundMath`, `GoalMath`) is pure functions over `AssetValuation` — no I/O, no re-fetching prices.

```mermaid
erDiagram
    POSITION ||--o{ ASSET_VALUATION : "valued over time"
    TARGET_ALLOCATION ||--o{ TARGET_ALLOCATION_LINE : has
    POSITION {
        uuid asset_id PK "target - spec-03, read model"
        uuid portfolio_id
        uuid user_id
        string asset_class
        string valuation_mode
        uuid instrument_id
        numeric quantity
        bool portfolio_is_archived
        datetime updated_at
    }
    VALUATION_SNAPSHOT {
        uuid id PK
        uuid user_id
        uuid portfolio_id
        date date
        numeric total_pln
        bool is_stale
    }
    ASSET_VALUATION {
        uuid id PK "target - spec-03"
        uuid asset_id FK
        date date
        string asset_class
        numeric quantity
        numeric price_used
        numeric fx_rate_used
        numeric value_pln
        bool is_stale
    }
    TARGET_ALLOCATION {
        uuid id PK "target - insights spec"
        uuid user_id
        uuid portfolio_id "null = total wealth"
    }
    TARGET_ALLOCATION_LINE {
        uuid id PK
        string asset_class
        numeric target_percent
        numeric tolerance_band
    }
    EMERGENCY_FUND {
        uuid user_id PK "target - insights spec"
        numeric monthly_expenses
        int target_months
    }
    SAVINGS_GOAL {
        uuid id PK "target - insights spec"
        uuid user_id
        numeric target_amount
        date deadline
        numeric annual_return_rate
    }
```

## Valuation algorithm

```
asset value (PLN) =
  if Market:            Quantity × last PriceQuote × FxRate(quote currency → PLN, same date)
  if Manual:             ManualValueAmount × FxRate(currency → PLN, snapshot date)
  if Currency-valued:   Quantity × FxRate(currency → PLN, snapshot date)

portfolio value = Σ assets ; net worth = Σ portfolios
```

No price for a given day → use the last known one (weekends, holidays); mark `IsStale` when the price or rate used is more than 7 days old. Unchanged by the redesign — only *where* it runs moves, from Reporting querying Portfolio/MarketData live to Reporting valuing its own `Position` rows against a daily price/FX batch.

## Invariants

- `TargetAllocationLine` percentages within one allocation sum to exactly 100.
- A `Sell` transaction cannot take asset quantity below 0, checked across the asset's full transaction history.
- Amounts and quantities ≥ 0; fees ≥ 0.
- One `PriceQuote` per (instrument, date); one `FxRate` per (pair, date) — unique indexes.
- Every user-owned entity carries `UserId` from the JWT — enforced by an architecture test, never trusted from the request body (ADR-006).
- A user-chosen currency (`Portfolio.Currency`, `Asset.Currency`) is one of `SupportedCurrencies` (PLN/EUR/USD, default PLN, canonical uppercase) — validated on write only; this does not constrain `Instrument.QuoteCurrency` or `FxRate.Pair`.
- `Asset.ValuationMode` is exactly one of Market / Manual / Currency-valued, enforced by request validation, not a DB constraint.

## Conscious simplifications

- **Single-entry transactions**: `Dividend`/`Interest` do not create an offsetting cash-asset flow — they record that income happened, not where the cash landed. Modeling a full double-entry ledger was judged not worth it for a personal-use app.
- **Fees in the asset's own currency**: a `Transaction.FeeAmount` is denominated in `Asset.Currency`, never a separate currency — avoids a second FX lookup per transaction.
- **PLN-only base currency**: every valuation ends in PLN (ADR-008); there is no per-user reporting currency.

## Cross-service reference rule (ADR-003)

A reference to another service's entity (`Asset.InstrumentId` → MarketData, `Position.AssetId`/`PortfolioId` → Portfolio) is a plain `Guid` column with **no foreign key and no navigation property** — consistency is enforced by the API/event contract, not the database. This is a deliberate, accepted cost of database-per-service: a dangling reference is a valid state (e.g. an asset deleted in Portfolio after Reporting already has a `Position` row for it) and every reader must treat it as a normal case, not a bug.
