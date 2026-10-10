using Quartz;

namespace Skarbiec.MarketData.Sources;

public sealed class QuartzHistoryBackfillTrigger(ISchedulerFactory schedulerFactory) : IHistoryBackfillTrigger
{
    public Task TriggerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
