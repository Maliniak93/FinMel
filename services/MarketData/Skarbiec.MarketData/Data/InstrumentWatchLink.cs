namespace Skarbiec.MarketData.Data;

// IsRemoved is a terminal tombstone: a watchlist item id is unique per add and never comes back.
public sealed class InstrumentWatchLink
{
    public required Guid WatchlistItemId { get; init; }

    public Guid InstrumentId { get; set; }

    public bool IsRemoved { get; set; }
}
