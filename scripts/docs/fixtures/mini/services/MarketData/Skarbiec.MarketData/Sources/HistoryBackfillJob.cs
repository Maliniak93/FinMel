using Quartz;

namespace Skarbiec.MarketData.Sources;

public sealed class HistoryBackfillJob : IJob
{
    public static readonly JobKey Key = new("history-backfill", "market-data");

    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
