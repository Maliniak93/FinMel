using Skarbiec.MarketData.Data;
using Skarbiec.ServiceDefaults.Messaging;

namespace Skarbiec.MarketData.Messaging;

public sealed class WatchlistItemAddedConsumerDefinition
    : IdempotentConsumerDefinition<WatchlistItemAddedConsumer, MarketDataDbContext>
{
    public WatchlistItemAddedConsumerDefinition() => Endpoint(e => e.Name = "market-data-watchlist-item-added");
}
