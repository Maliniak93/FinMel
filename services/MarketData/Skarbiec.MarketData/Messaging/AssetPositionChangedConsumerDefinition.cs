using Skarbiec.MarketData.Data;
using Skarbiec.ServiceDefaults.Messaging;

namespace Skarbiec.MarketData.Messaging;

// Explicit queue: Reporting has a consumer of the same name, and one shared queue would split the events between the services.
public sealed class AssetPositionChangedConsumerDefinition
    : IdempotentConsumerDefinition<AssetPositionChangedConsumer, MarketDataDbContext>
{
    public AssetPositionChangedConsumerDefinition() => Endpoint(e => e.Name = "market-data-asset-position-changed");
}
