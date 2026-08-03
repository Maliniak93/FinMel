namespace Skarbiec.MarketData.Data;

// InstrumentId is a plain Guid — no navigation/FK, same aggregate-isolation convention Portfolio
// uses between Asset and Transaction, even though both entities live in this same database.
public sealed class PriceQuote
{
    public required Guid Id { get; init; }
    public required Guid InstrumentId { get; set; }
    public required DateOnly Date { get; set; }
    public required decimal Close { get; set; }
}
