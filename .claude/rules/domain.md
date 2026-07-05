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
- Transaction types: Buy, Sell, Deposit, Withdraw, Dividend, Interest, Fee. Asset quantity is derived from transactions (recomputed, never stored as truth). Manual-valuation assets may exist without transactions.
- Two valuation modes: market (`Quantity × last PriceQuote × FxRate`, same date) and manual (`ManualValue × FxRate` at snapshot date).
- Missing price for a date → use last known (weekends/holidays); mark `stale` when older than 7 days.

## Invariants (validate and test)

- TargetAllocation percentages sum to 100; tolerance band per position.
- Sell cannot take asset quantity below 0.
- Amounts and quantities ≥ 0; fees ≥ 0.
- One PriceQuote per (instrument, date); one FxRate per (pair, date) — unique indexes.
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
