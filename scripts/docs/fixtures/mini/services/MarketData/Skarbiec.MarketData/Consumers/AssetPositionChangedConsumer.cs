namespace Skarbiec.MarketData.Consumers;

public sealed class AssetPositionChangedConsumer : IConsumer<AssetPositionChanged>
{
    public Task Consume(ConsumeContext<AssetPositionChanged> context) => Task.CompletedTask;
}

public sealed class AssetPositionChangedConsumerDefinition : IdempotentConsumerDefinition<AssetPositionChangedConsumer, MarketDataDbContext>;
