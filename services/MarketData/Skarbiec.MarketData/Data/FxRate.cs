namespace Skarbiec.MarketData.Data;

// Pair is a plain 6-letter code (e.g. "USDPLN") — NBP table A convention, quote currency vs. PLN
// base currency (ADR-008).
public sealed class FxRate
{
    public required Guid Id { get; init; }
    public required string Pair { get; set; }
    public required DateOnly Date { get; set; }
    public required decimal Rate { get; set; }
}
