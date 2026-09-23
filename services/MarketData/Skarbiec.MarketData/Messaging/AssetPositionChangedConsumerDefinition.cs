using Skarbiec.MarketData.Data;
using Skarbiec.ServiceDefaults.Messaging;

namespace Skarbiec.MarketData.Messaging;

/// <summary>T0.12 inbox template — see <c>IdempotentConsumerDefinition</c> for what this wires onto the consumer's receive endpoint.</summary>
/// <remarks>
/// Explicit queue name: Reporting has a consumer class of the same name, and the kebab-case formatter
/// would give both services the one queue <c>asset-position-changed</c> — competing consumers, each
/// service seeing only part of the events. Each subscribing service needs its own queue.
/// </remarks>
public sealed class AssetPositionChangedConsumerDefinition
    : IdempotentConsumerDefinition<AssetPositionChangedConsumer, MarketDataDbContext>
{
    public AssetPositionChangedConsumerDefinition() => Endpoint(e => e.Name = "market-data-asset-position-changed");
}
