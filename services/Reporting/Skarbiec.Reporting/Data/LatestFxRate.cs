namespace Skarbiec.Reporting.Data;

// Global reference data from the daily batch, so the position-event path converts without calling MarketData; an older Date never overwrites.
public sealed class LatestFxRate
{
    // Canonical uppercase, e.g. USDPLN.
    public required string Pair { get; init; }

    public required DateOnly Date { get; set; }

    public required decimal Rate { get; set; }
}
