namespace Skarbiec.Reporting.Messaging;

public sealed class DailyPricesSyncedConsumer : IConsumer<DailyPricesSynced>
{
    public Task Consume(ConsumeContext<DailyPricesSynced> context) => Task.CompletedTask;
}

public sealed class AssetPositionChangedConsumer : IConsumer<AssetPositionChanged>
{
    public Task Consume(ConsumeContext<AssetPositionChanged> context) => Task.CompletedTask;
}

public sealed class PortfolioArchivedConsumer : IConsumer<PortfolioArchived>
{
    public Task Consume(ConsumeContext<PortfolioArchived> context) => Task.CompletedTask;
}

public sealed class PortfolioHistoryRebuildConsumer : IConsumer<PortfolioHistoryRebuildRequested>
{
    public Task Consume(ConsumeContext<PortfolioHistoryRebuildRequested> context) => Task.CompletedTask;
}
