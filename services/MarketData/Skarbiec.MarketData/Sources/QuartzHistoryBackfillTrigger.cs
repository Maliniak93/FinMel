using System.Globalization;
using Quartz;

namespace Skarbiec.MarketData.Sources;

// A non-durable job and a one-shot trigger: Quartz removes both once the fire completes.
public sealed class QuartzHistoryBackfillTrigger(ISchedulerFactory schedulerFactory) : IHistoryBackfillTrigger
{
    public async Task EnqueueAsync(Guid instrumentId, DateOnly from, CancellationToken cancellationToken)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);

        var jobDetail = JobBuilder.Create<HistoryBackfillJob>()
            .WithIdentity($"history-backfill-{instrumentId}-{Guid.NewGuid()}", "market-data")
            .UsingJobData(HistoryBackfillJob.InstrumentIdDataKey, instrumentId.ToString())
            .UsingJobData(HistoryBackfillJob.FromDataKey, from.ToString("O", CultureInfo.InvariantCulture))
            .Build();

        var trigger = TriggerBuilder.Create()
            .ForJob(jobDetail)
            .WithIdentity($"history-backfill-trigger-{instrumentId}-{Guid.NewGuid()}", "market-data")
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(jobDetail, trigger, cancellationToken: cancellationToken);
    }
}
