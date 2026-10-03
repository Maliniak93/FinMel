namespace Skarbiec.MarketData.Data;

// Pair is a 6-letter code such as "USDPLN": the quote currency against PLN.
public sealed class FxRate
{
    public required Guid Id { get; init; }
    public required string Pair { get; set; }
    public required DateOnly Date { get; set; }
    public required decimal Rate { get; set; }
}
