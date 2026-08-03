namespace Skarbiec.MarketData.Sources;

/// <summary>A single day's close for one instrument, as returned by an <see cref="IPriceSource"/>
/// before PriceSyncJob (T2.6) upserts it into <see cref="Data.PriceQuote"/>.</summary>
public sealed record InstrumentQuote(Guid InstrumentId, DateOnly Date, decimal Close);
