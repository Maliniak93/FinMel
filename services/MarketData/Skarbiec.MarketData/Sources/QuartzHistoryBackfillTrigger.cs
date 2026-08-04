using Quartz;

namespace Skarbiec.MarketData.Sources;

/// <summary>
/// <see cref="IHistoryBackfillTrigger"/> over Quartz: builds a fresh, non-durable
/// <see cref="HistoryBackfillJob"/> + one-shot trigger pair per call (T2.7 scope: "enqueue a one-off
/// Quartz job") and hands it to the scheduler. Non-durable means Quartz removes both job and trigger
/// on its own once the single fire completes — no cleanup needed here. <see cref="EnqueueAsync"/>
/// itself never touches <see cref="IPriceSource"/>/<see cref="IFxRateSource"/> — scheduling a trigger
/// is all that happens on this call stack, so it returns before any external API call happens.
/// </summary>
public sealed class QuartzHistoryBackfillTrigger(ISchedulerFactory schedulerFactory) : IHistoryBackfillTrigger
{
    public async Task EnqueueAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);

        var jobDetail = JobBuilder.Create<HistoryBackfillJob>()
            .WithIdentity($"history-backfill-{instrumentId}-{Guid.NewGuid()}", "market-data")
            .UsingJobData(HistoryBackfillJob.InstrumentIdDataKey, instrumentId.ToString())
            .Build();

        var trigger = TriggerBuilder.Create()
            .ForJob(jobDetail)
            .WithIdentity($"history-backfill-trigger-{instrumentId}-{Guid.NewGuid()}", "market-data")
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(jobDetail, trigger, cancellationToken);
    }
}
