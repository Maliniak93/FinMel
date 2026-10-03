namespace Skarbiec.Reporting.Valuation;

/// <summary>Latest quote on or before the snapshot date; Date decides staleness.</summary>
public sealed record InstrumentPriceLookup(string QuoteCurrency, DateOnly Date, decimal Close);

/// <summary>Latest FX rate on or before the snapshot date.</summary>
public sealed record FxRateLookup(DateOnly Date, decimal Rate);
