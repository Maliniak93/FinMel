using Skarbiec.MarketData.Data;
using Skarbiec.ServiceDefaults.Messaging;

namespace Skarbiec.MarketData.Messaging;

public sealed class WatchlistItemRemovedConsumerDefinition
    : IdempotentConsumerDefinition<WatchlistItemRemovedConsumer, MarketDataDbContext>
{
    public WatchlistItemRemovedConsumerDefinition() => Endpoint(e => e.Name = "market-data-watchlist-item-removed");
}
