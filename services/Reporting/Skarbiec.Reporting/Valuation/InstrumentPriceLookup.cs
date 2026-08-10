namespace Skarbiec.Reporting.Valuation;

/// <summary>Latest quote at or before the snapshot date (the "no quote today → last known" fallback already resolved by MarketData's batch query) — <see cref="Date"/> is compared against the snapshot date to decide staleness.</summary>
public sealed record InstrumentPriceLookup(string QuoteCurrency, DateOnly Date, decimal Close);

/// <summary>Latest FX rate at or before the snapshot date, same "already resolved" shape as <see cref="InstrumentPriceLookup"/>.</summary>
public sealed record FxRateLookup(DateOnly Date, decimal Rate);
