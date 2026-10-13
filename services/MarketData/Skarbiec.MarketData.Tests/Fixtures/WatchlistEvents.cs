using Skarbiec.Contracts.Events;

namespace Skarbiec.MarketData.Tests.Fixtures;

public static class WatchlistEvents
{
    public static WatchlistItemAdded Added(Guid watchlistItemId, Guid instrumentId, Guid? userId = null) => new()
    {
        WatchlistItemId = watchlistItemId,
        UserId = userId ?? Guid.NewGuid(),
        InstrumentId = instrumentId,
        OccurredAtUtc = DateTimeOffset.UtcNow,
    };

    public static WatchlistItemRemoved Removed(Guid watchlistItemId, Guid instrumentId, Guid? userId = null) => new()
    {
        WatchlistItemId = watchlistItemId,
        UserId = userId ?? Guid.NewGuid(),
        InstrumentId = instrumentId,
        OccurredAtUtc = DateTimeOffset.UtcNow,
    };
}
