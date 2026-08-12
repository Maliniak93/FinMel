---
paths:
  - "services/**"
  - "contracts/**"
  - "gateway/**"
  - "web/**"
---

# Skarbiec domain model & invariants

## Domain

- `AssetClass` enum (SharedKernel): Cash, Deposit, Stock, Etf, Bond, Crypto, PreciousMetal, RealEstate, Other.
- Transaction types: Buy, Sell, Deposit, Withdraw, Dividend, Interest, Fee. Asset quantity is derived from transactions (recomputed, never stored as truth). Manual-valuation and currency-valued assets may exist without transactions.
- Three valuation modes (M1.4), explicit on `Asset.ValuationMode` (`AssetValuationMode` in `Skarbiec.Contracts`): market (`Quantity × last PriceQuote × FxRate`, same date), manual (`ManualValue × FxRate` at snapshot date), and currency-valued (`Quantity × FxRate` at snapshot date — no instrument, no manual amount; natural members are Cash/Deposit, per `AssetValuationModes.Default`, but every class may still use market/manual as before).
- Missing price/FX rate for a date → use last known (weekends/holidays); mark `stale` when older than 7 days. MarketData's `PriceSyncJob` syncs FX for every `SupportedCurrencies` code daily (not only currencies an `Instrument` happens to quote), so a currency-valued asset's rate stays current.

## Invariants (validate and test)

- TargetAllocation percentages sum to 100; tolerance band per position.
- Sell cannot take asset quantity below 0.
- Amounts and quantities ≥ 0; fees ≥ 0.
- One PriceQuote per (instrument, date); one FxRate per (pair, date) — unique indexes.
- User-chosen currency (Portfolio, Asset) comes from `SupportedCurrencies` in `Skarbiec.Contracts` — PLN/EUR/USD, default PLN, uppercase, one definition (frontend mirror: `web/src/app/shared/currencies.ts`). MarketData's own currencies (`Instrument.QuoteCurrency`, `FxRate.Pair`) are **not** restricted.
- Every user-owned entity has `UserId` — enforced by a NetArchTest architecture test.

## Data ownership

- Identity: User. Portfolio: Portfolio, Asset, Transaction. MarketData: Instrument, PriceQuote, FxRate. Strategy: TargetAllocation, EmergencyFund, SavingsGoal (RebalancingSuggestion computed on demand, not persisted). Reporting: ValuationSnapshot (read model, JSONB breakdown per asset class).
- Cross-service references are plain Guids, no FK (e.g. `Asset.InstrumentId` → MarketData).

## Testing

- xUnit + Testcontainers (PostgreSQL, RabbitMQ). Tenancy isolation test per service is part of DoD.
- Contract tests for event deserialization; NetArchTest for architecture rules; Playwright e2e through the Gateway.

## Misc

- Rebalancing suggestions are information, never investment advice — include the disclaimer.
- Secrets: user-secrets locally, `.env` on the VPS — never in the repo.

## Related rules

Code-style rules live in `dotnet.md` and `angular.md` in this directory — this file covers domain and invariants only.
