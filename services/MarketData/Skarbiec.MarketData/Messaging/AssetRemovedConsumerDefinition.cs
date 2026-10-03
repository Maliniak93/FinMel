using Skarbiec.MarketData.Data;
using Skarbiec.ServiceDefaults.Messaging;

namespace Skarbiec.MarketData.Messaging;

// Explicit queue: Reporting's AssetRemovedConsumer would otherwise share the asset-removed queue.
public sealed class AssetRemovedConsumerDefinition
    : IdempotentConsumerDefinition<AssetRemovedConsumer, MarketDataDbContext>
{
    public AssetRemovedConsumerDefinition() => Endpoint(e => e.Name = "market-data-asset-removed");
}
