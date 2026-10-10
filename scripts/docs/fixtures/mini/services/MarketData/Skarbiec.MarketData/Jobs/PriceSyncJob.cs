namespace Skarbiec.MarketData.Jobs;

public sealed class PriceSyncJob(IPublishEndpoint publishEndpoint)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await publishEndpoint.Publish(new DailyPricesSynced { Date = DateOnly.MinValue }, cancellationToken);
    }
}
