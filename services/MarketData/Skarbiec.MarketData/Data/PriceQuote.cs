namespace Skarbiec.MarketData.Data;

// No FK to Instrument, even within this database.
public sealed class PriceQuote
{
    public required Guid Id { get; init; }
    public required Guid InstrumentId { get; set; }
    public required DateOnly Date { get; set; }
    public required decimal Close { get; set; }
}
