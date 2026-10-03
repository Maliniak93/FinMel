namespace Skarbiec.Reporting.Data;

// Global reference data from the daily batch, so the position-event path values without calling MarketData; an older Date never overwrites.
public sealed class LatestInstrumentPrice
{
    public required Guid InstrumentId { get; init; }

    public required string QuoteCurrency { get; set; }

    public required DateOnly Date { get; set; }

    public required decimal Close { get; set; }
}
