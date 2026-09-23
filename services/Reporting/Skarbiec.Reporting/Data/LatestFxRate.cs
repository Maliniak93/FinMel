namespace Skarbiec.Reporting.Data;

/// <summary>
/// The last currency→PLN rate Reporting has seen for one pair, kept from the daily
/// <c>DailyPricesSynced</c> batch (spec-07, ADR-025) so the position-event path can convert a
/// foreign-currency asset without calling MarketData — ADR-021's two REST uses stay two.
/// </summary>
/// <remarks>
/// Global reference data, not user data: no <c>UserId</c>, no tenancy query filter (spec-07 design
/// decision 4 — ADR-006 covers user-owned entities only). One row per pair; a batch returning an
/// older <see cref="Date"/> than the stored one never overwrites it.
/// </remarks>
public sealed class LatestFxRate
{
    /// <summary>Primary key: the canonical uppercase pair, e.g. <c>USDPLN</c> — same shape as MarketData's <c>FxRate.Pair</c>.</summary>
    public required string Pair { get; init; }

    /// <summary>The rate's own date — compared against the valuation date for staleness.</summary>
    public required DateOnly Date { get; set; }

    public required decimal Rate { get; set; }
}
