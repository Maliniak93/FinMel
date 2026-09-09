# 03 — Domain model

Full ERD diagram: `diagrams/erd.mermaid`.

**Data ownership (microservices, ADR-003):** each service has its own database and is the sole owner of its entities. Cross-service references only by ID, **no foreign keys** (e.g. `Asset.InstrumentId` is a Guid from the MarketData database — consistency is enforced by the API, not the database). On the ERD, relationships crossing service boundaries are dashed lines / described as "ref by id".

## Asset classes

`AssetClass` (enum in SharedKernel): `Cash`, `Deposit` (term deposits), `Stock`, `Etf`, `Bond` (including retail treasury bonds), `Crypto`, `PreciousMetal`, `RealEstate`, `Other`.

Three asset valuation modes (`AssetValuationMode` in `Skarbiec.Contracts`, explicit on `Asset.ValuationMode` since M1.4 — before then, market vs. manual was implicit from `InstrumentId is not null`, which had no way to represent a third shape):

1. **Market** — the asset points to an `Instrument` (ticker + source); value = quantity × last price × FX rate.
2. **Manual** — `manual_value` + update date (real estate, collections, private loans). The UI reminds the user to refresh it every N months.
3. **Currency-valued** (M1.4) — no instrument, no manual value; value = `quantity` × FX rate for the asset's own `currency`. For the base currency (PLN) the "rate" is 1, so no FX lookup is needed at all. Natural members: Cash, a term Deposit — plain money held in a currency, not priced by a market or refreshed by hand.

The asset-class → default-mode mapping (`AssetValuationModes.Default` in `Skarbiec.Contracts`, M1.4) is a *default*, not a hard constraint — Market and Manual stay available to every class exactly as before M1.4; only the currency-valued combination (neither `InstrumentId` nor a manual value) is gated by class, since "neither" was always a validation error before this mode existed:

| Default mode | Classes |
| --- | --- |
| Currency-valued | Cash, Deposit |
| Market | Stock, Etf, Bond, Crypto, PreciousMetal |
| Manual | RealEstate, Other |

## Entities per service

### Identity (`identity_db`)
- **User** — ASP.NET Identity + `DisplayName`, `BaseCurrency` (default PLN). Publishes `UserRegistered`.

### Portfolio (`portfolio_db`)
- **Portfolio** — `UserId`, name, description, currency. A user has many portfolios.
- **Asset** — `PortfolioId`, `AssetClass`, `ValuationMode` (M1.4), name, currency, `Quantity`, plus exactly one of: `InstrumentId` (Guid from MarketData, **no FK** — different database) for Market, `ManualValue`+`ManualValueDate` for Manual, or neither for Currency-valued (value comes straight from `Quantity` × FX rate — no extra column needed).
- **Transaction** — `AssetId`, type (`Buy`, `Sell`, `Deposit`, `Withdraw`, `Dividend`, `Interest`, `Fee`), quantity, unit price, fee, date. Asset quantity is **derived from transactions** ("light" event sourcing — state is recomputed, transactions are the source of truth). Exception: manual-valuation assets may exist without transactions.

### MarketData (`marketdata_db`)
- **Instrument** — ticker, name, source (`Nbp`, `Stooq`, `CoinGecko`), quote currency, asset class. A global dictionary (shared across users) + the ability to add custom ones.
- **PriceQuote** — `InstrumentId`, date, closing price. Unique index (instrument, date).
- **FxRate** — currency pair, date, rate (NBP table A). Unique index (pair, date).

### Strategy (`strategy_db`)
- **TargetAllocation** — scope (a portfolio or total wealth), positions: `AssetClass` → `TargetPercent` + `ToleranceBand` (e.g. 60% stocks ±5 pp). Positions sum to 100%.
- **RebalancingSuggestion** — computed on demand (not persisted): deviations and "buy/sell for X" amounts. Prefer rebalancing with new contributions (lower tax cost) — "where should I deposit X PLN?" mode.
- **EmergencyFund** — `UserId`, monthly expenses, target number of months (e.g. 6), list of designated assets (join `emergency_fund_asset`). Metric: % coverage.
- **SavingsGoal** — name, target amount, deadline, linked portfolio/assets, computed required monthly contribution.

### Reporting (`reporting_db`) — read model (CQRS)
- **ValuationSnapshot** — `PortfolioId`, date, value in the base currency + per-asset-class breakdown (JSONB). Created after the `DailyPricesSynced` event from MarketData: Reporting fetches positions from Portfolio (REST) and prices from MarketData (REST), computes and stores. This drives the history chart and the dashboard — no fan-out to 3 services on every user visit. Data is eventually consistent (daily granularity — acceptable).

## Invariants (examples)
- `TargetPercent` values in an allocation sum to 100.
- `Sell` cannot take asset quantity below 0.
- Amounts and quantities ≥ 0; fees ≥ 0.
- `PriceQuote`/`FxRate` — one entry per (instrument/pair, day).
- Every user-owned entity has `UserId` — enforced by an architecture test (NetArchTest).
- A **user-chosen** currency (`Portfolio.Currency`, `Asset.Currency`) must be one of `SupportedCurrencies` in `Skarbiec.Contracts` — `PLN`, `EUR`, `USD`, default `PLN`, canonical uppercase. This does **not** constrain MarketData: `Instrument.QuoteCurrency` and `FxRate.Pair` keep using whatever the provider quotes (GBP, CHF, …). Validated on writes only — rows predating the rule still read back unchanged.
- `Asset.ValuationMode` is exactly one of Market/Manual/Currency-valued (M1.4) — enforced by `AddAssetRequest`/`UpdateAssetRequest.Validate`, not a DB constraint. MarketData's daily `PriceSyncJob` syncs an FX rate for every `SupportedCurrencies` code (not only currencies an `Instrument` happens to quote), so a currency-valued asset's rate is never stuck on `MarketDataSeeder`'s 2020 bootstrap row.

## Valuation — algorithm

```
asset value (PLN) =
  if Market:           Quantity × last PriceQuote × FxRate(quote currency→PLN, same date)
  if Manual:            ManualValue × FxRate(currency→PLN, snapshot date)
  if Currency-valued:  Quantity × FxRate(currency→PLN, snapshot date)

portfolio value = Σ assets; net worth = Σ portfolios (minus liabilities, once we add loans)
```

No price for a given day → take the last known one (weekends, holidays), mark `stale` when > 7 days old.

## Future (don't build now, don't block)
- **Liabilities** (mortgage) — `Liability` symmetric to `Asset`; net worth = assets − liabilities. Worth adding early; the schema is ready for it.
- **Household** — an extra level above `UserId`; that's why the tenancy filter lives in one place.
- **FIFO/taxes** — transactions already store everything needed for PIT-38 settlement.
