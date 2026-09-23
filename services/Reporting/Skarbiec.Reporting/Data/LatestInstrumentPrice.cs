namespace Skarbiec.Reporting.Data;

/// <summary>
/// The last close Reporting has seen for one MarketData instrument, kept from the daily
/// <c>DailyPricesSynced</c> batch (spec-07, ADR-025) so the position-event path can value a market
/// asset without calling MarketData — ADR-021's two REST uses stay two.
/// </summary>
/// <remarks>
/// Global reference data, not user data: no <c>UserId</c>, no tenancy query filter (spec-07 design
/// decision 4 — ADR-006 covers user-owned entities only). One row per instrument; a batch returning
/// an older <see cref="Date"/> than the stored one never overwrites it.
/// </remarks>
public sealed class LatestInstrumentPrice
{
    /// <summary>Primary key: MarketData's instrument id — a cross-service reference, plain <see cref="Guid"/>, no FK (ADR-003).</summary>
    public required Guid InstrumentId { get; init; }

    public required string QuoteCurrency { get; set; }

    /// <summary>The quote's own date (the "last known" fallback MarketData already resolved) — compared against the valuation date for staleness.</summary>
    public required DateOnly Date { get; set; }

    public required decimal Close { get; set; }
}
