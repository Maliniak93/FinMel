using Skarbiec.MarketData.Data;
using Skarbiec.ServiceDefaults.Messaging;

namespace Skarbiec.MarketData.Messaging;

/// <summary>T0.12 inbox template — see <c>IdempotentConsumerDefinition</c> for what this wires onto the consumer's receive endpoint.</summary>
/// <remarks>Explicit queue name for the same reason as <see cref="AssetPositionChangedConsumerDefinition"/>:
/// Reporting's <c>AssetRemovedConsumer</c> would otherwise share the <c>asset-removed</c> queue.</remarks>
public sealed class AssetRemovedConsumerDefinition
    : IdempotentConsumerDefinition<AssetRemovedConsumer, MarketDataDbContext>
{
    public AssetRemovedConsumerDefinition() => Endpoint(e => e.Name = "market-data-asset-removed");
}
