namespace Skarbiec.MarketData.Sources;

/// <summary>One currency pair's rate for one day (e.g. "USDPLN"), matching <see cref="Data.FxRate"/>'s
/// (pair, date) convention, as returned by an <see cref="IFxRateSource"/> before PriceSyncJob (T2.6)
/// upserts it.</summary>
public sealed record FxRateQuote(string Pair, DateOnly Date, decimal Rate);
