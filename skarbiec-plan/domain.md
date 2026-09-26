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
| `Portfolio` | `Id, UserId, Name, Description?, Currency, IsArchived` | `AssetCount` removed (spec-02); delete cascades to its assets and their transactions in the handler (spec-08) |
| `Asset` | `Id, UserId, PortfolioId, AssetClass, ValuationMode, Name, Currency, Quantity, ManualValueAmount?, ManualValueDate?, InstrumentId?, Version, xmin` | `ValuationMode` already explicit (M1.4); `TransactionCount` removed (spec-02); delete cascades to its transactions in the handler (spec-08); `Version` is the per-asset event-ordering counter `PositionEventPublisher` bumps (not `xmin`, which doesn't move on the archive/restore fan-out) |
| `Transaction` | `Id, UserId, AssetId, Type, Quantity, UnitPriceAmount, FxRateToPln?, Date, xmin` | `FeeAmount` removed; `FxRateToPln` is the `{Asset.Currency}PLN` rate frozen at write time — the latest MarketData rate on or before `Date`, `1` for PLN, `null` when MarketData has none that early (ADR-026) |
| `TermDeposit` | `AssetId (PK, FK → Asset), UserId, BankName?, Principal, StartDate, TermLength, TermUnit (Days \| Months), MaturityDate, AnnualInterestRatePercent, Capitalization (AtMaturity \| Monthly \| Quarterly \| Yearly), TaxExempt, EarlyBreakInterestLossPercent` | new (term-deposits): the terms of a Deposit-class asset, 1:1 with it and the one real FK in `portfolio_db` (cascade delete); `MaturityDate` is derived on every write; the projection (`DepositInterestMath`: actual/365, gross per capitalisation period rounded half-away-from-zero to grosze, 19 % Belka tax per period rounded up, net compounded) and the `Active \| Due` status (Europe/Warsaw date) are computed at read time, never stored |

Every slice that mutates a position publishes `AssetPositionChanged` in the same transaction as the write (spec-02); removal publishes `AssetRemoved` (deleting its transactions with it), deleting a portfolio publishes `PortfolioDeleted` plus one `AssetRemoved { CascadedFromPortfolio = true }` per asset (spec-08), and archive/restore publish `PortfolioArchived`/`PortfolioRestored` plus one `AssetPositionChanged` per asset carrying the new archived flag. An archived portfolio is read-only: adding, updating or removing its assets, and recording, updating or deleting their transactions, returns 409 `Conflict.PortfolioArchived` until it is restored. Renaming or deleting the portfolio itself stays allowed.

A Deposit-class asset is a term deposit, created and edited only through the deposit slices (`AddDeposit`, `UpdateDeposit`): they write the `Asset`, its `TermDeposit` and a system-managed opening `Deposit` transaction (principal, start date, unit price 1) together. `UpdateDeposit` rewrites that opening transaction instead of adding a correction; `RemoveAsset` deletes the `TermDeposit` with the asset.

```mermaid
erDiagram
    PORTFOLIO ||--o{ ASSET : contains
    ASSET ||--o{ TRANSACTION : records
    ASSET ||--o| TERM_DEPOSIT : "Deposit class only"
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
        bigint version "AssetPositionChanged ordering"
        xid xmin "concurrency token"
    }
    TRANSACTION {
        uuid id PK
        uuid user_id
        uuid asset_id FK
        string type "Buy Sell Deposit Withdraw Dividend Interest"
        numeric quantity
        numeric unit_price_amount
        numeric fx_rate_to_pln "null = no rate on or before date"
        date date
        xid xmin "concurrency token"
    }
    TERM_DEPOSIT {
        uuid asset_id PK, FK
        uuid user_id
        string bank_name
        numeric principal
        date start_date
        int term_length
        string term_unit "Days Months"
        date maturity_date "derived on write"
        numeric annual_interest_rate_percent
        string capitalization "AtMaturity Monthly Quarterly Yearly"
        bool tax_exempt "IKE/IKZE"
        numeric early_break_interest_loss_percent
    }
```

## MarketData (`marketdata_db`)

| Entity | Fields | Role |
|---|---|---|
| `Currency` | `Code (PK), Name, Symbol, DecimalPlaces, DisplayOrder` | no `FallbackRateToPln`, no `IsPivot` (PLN is the fixed base). Seed: PLN, EUR, USD, GBP, CHF |
| `Instrument` | `Id, Ticker, Name, Source, QuoteCurrency, AssetClass, VerificationStatus` | unchanged; `QuoteCurrency` is unconstrained (whatever the provider quotes) |
| `PriceQuote` | `InstrumentId, Date, ClosePrice` — unique (instrument, date) | unchanged |
| `FxRate` | `Pair (e.g. USDPLN), Date, Rate` — unique (pair, date) | unchanged |
| `SyncRun` | + `Kind: Prices \| Fx \| Backfill` | one log shape for all three jobs |
| `InstrumentUsage` | `InstrumentId (PK), AssetCount, FirstUsedAt` | derived from `AssetInstrumentLink`; `PriceSyncJob` syncs only `AssetCount > 0` |
| `AssetInstrumentLink` | `AssetId (PK), InstrumentId?, Version, IsRemoved` | per-asset state from the `AssetPositionChanged`/`AssetRemoved` consumers — makes them idempotent and order-safe (version compare, terminal `IsRemoved` tombstone) |

Jobs: `FxSyncJob` daily over every catalog currency, 12-month backfill on a currency's first run; `PriceSyncJob` syncs only instruments in use; `HistoryBackfillJob` fires for a newly created custom instrument and from the `InstrumentUsage` consumer on an instrument's first use. All three write a `SyncRun` and respect `Testing:DisableBackgroundJobs`.

```mermaid
erDiagram
    INSTRUMENT ||--o{ PRICE_QUOTE : "has quotes"
    INSTRUMENT ||--o| INSTRUMENT_USAGE : "tracked by"
    CURRENCY ||--o{ FX_RATE : "quoted via pair"
    CURRENCY {
        string code PK
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
        uuid instrument_id PK
        int asset_count
        datetime first_used_at
    }
    SYNC_RUN {
        uuid id PK
        string kind "Prices Fx Backfill"
        datetime started_at
        bool success
    }
```

## Reporting (`reporting_db`)

| Entity | Fields | Role |
|---|---|---|
| `Position` | copy of the `AssetPositionChanged` payload + `UpdatedAt`; `AssetId` is the primary key | upserted from the inbox; `PortfolioIsArchived` kept current from `Portfolio*` events |
| `ValuationSnapshot` | `Id, UserId, PortfolioId, Date, TotalPln, IsStale` | breakdown is a `GROUP BY AssetClass` over `AssetValuation` lines, not a stored JSON blob; written by the daily sync, and for today also by every position event from the `Latest*` tables (ADR-025); `PortfolioArchived` zeroes today's snapshot, so an archived portfolio drops out of net worth while its earlier snapshots stay |
| `AssetValuation` | `Id, UserId, PortfolioId, AssetId, Date, AssetClass, Quantity, PriceUsed?, PriceDate?, FxRateUsed?, ValuePln, IsStale`; unique `(AssetId, Date)` | the basis for P/L per asset, emergency fund and goal math, and TWR — nothing needs recomputing from scratch |
| `LatestInstrumentPrice` | `InstrumentId` (PK), `QuoteCurrency, Date, Close` | last close seen in the daily batch — global reference data, no `UserId` (ADR-025) |
| `LatestFxRate` | `Pair` (PK, e.g. `USDPLN`), `Date, Rate` | last rate seen in the daily batch — global reference data, no `UserId` (ADR-025) |
| `TargetAllocation` + `TargetAllocationLine` | scope (`PortfolioId?`, null = total), lines `AssetClass → TargetPercent, ToleranceBand`; unique `(UserId, PortfolioId?)` | **(target — insights spec, after spec-03)** |
| `EmergencyFund` + `EmergencyFundAsset` | `MonthlyExpenses, TargetMonths` + designated assets, validated locally against `Position` | **(target — insights spec)** |
| `SavingsGoal` + `SavingsGoalAsset` | `Name, TargetAmount, Deadline, AnnualReturnRate` + linked assets | **(target — insights spec)** |

Insight math (`AllocationMath`, `RebalancingMath`, `ContributionPlanMath`, `EmergencyFundMath`, `GoalMath`) is pure functions over `AssetValuation` — no I/O, no re-fetching prices.

```mermaid
erDiagram
    POSITION ||--o{ ASSET_VALUATION : "valued over time"
    TARGET_ALLOCATION ||--o{ TARGET_ALLOCATION_LINE : has
    POSITION {
        uuid asset_id PK "read model"
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
        uuid id PK
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
    LATEST_INSTRUMENT_PRICE {
        uuid instrument_id PK "global reference data"
        string quote_currency
        date date
        numeric close
    }
    LATEST_FX_RATE {
        string pair PK "global reference data"
        date date
        numeric rate
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
- Amounts and quantities ≥ 0.
- An asset's currency is immutable once it has transactions — each transaction's frozen PLN rate belongs to that currency (ADR-026).
- A Cash/Deposit asset's transactions are only Deposit/Withdraw.
- A Deposit-class asset has exactly one `TermDeposit`; its transactions are system-managed — record/update/delete on it is 409 `Conflict.DepositTransactionsManaged`, and `AddAsset`/`UpdateAsset` reject class Deposit (or a change to or from it) with 400 `Validation.UseDepositEndpoints`.
- One `PriceQuote` per (instrument, date); one `FxRate` per (pair, date) — unique indexes.
- Every user-owned entity carries `UserId` from the JWT — enforced by an architecture test, never trusted from the request body (ADR-006).
- A user-chosen currency (`Portfolio.Currency`, `Asset.Currency`) is one of `SupportedCurrencies` (PLN/EUR/USD, default PLN, canonical uppercase) — validated on write only; this does not constrain `Instrument.QuoteCurrency` or `FxRate.Pair`.
- `Asset.ValuationMode` is exactly one of Market / Manual / Currency-valued, enforced by request validation, not a DB constraint.

## Conscious simplifications

- **Single-entry transactions**: `Dividend`/`Interest` do not create an offsetting cash-asset flow — they record that income happened, not where the cash landed. Modeling a full double-entry ledger was judged not worth it for a personal-use app.
- **PLN value frozen at the transaction-date rate**: a transaction's PLN value is `Quantity × UnitPriceAmount × FxRateToPln`, with the rate fixed when the transaction is written and never recomputed later (e.g. after an older-history backfill) — a `null` rate fills in only when that transaction is edited (ADR-026).
- **PLN-only base currency**: every valuation ends in PLN (ADR-008); there is no per-user reporting currency.

## Cross-service reference rule (ADR-003)

A reference to another service's entity (`Asset.InstrumentId` → MarketData, `Position.AssetId`/`PortfolioId` → Portfolio) is a plain `Guid` column with **no foreign key and no navigation property** — consistency is enforced by the API/event contract, not the database. This is a deliberate, accepted cost of database-per-service: a dangling reference is a valid state (e.g. an asset deleted in Portfolio after Reporting already has a `Position` row for it) and every reader must treat it as a normal case, not a bug.
