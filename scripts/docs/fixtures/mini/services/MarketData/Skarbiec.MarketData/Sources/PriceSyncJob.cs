using Quartz;

namespace Skarbiec.MarketData.Sources;

public sealed class PriceSyncJob(IEnumerable<IPriceSource> sources, IPublishEndpoint publishEndpoint)
{
    public static readonly JobKey Key = new("price-sync", "market-data");

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            await source.FetchAsync(cancellationToken);
        }

        await publishEndpoint.Publish(new DailyPricesSynced { Date = DateOnly.MinValue }, cancellationToken);
    }
}
