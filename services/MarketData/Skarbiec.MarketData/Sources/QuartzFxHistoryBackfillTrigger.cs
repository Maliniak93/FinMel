using Quartz;

namespace Skarbiec.MarketData.Sources;

// One durable job under a fixed key: [DisallowConcurrentExecution] only holds per job detail, so a second fire queues behind the first.
public sealed class QuartzFxHistoryBackfillTrigger(ISchedulerFactory schedulerFactory) : IFxHistoryBackfillTrigger
{
    public static readonly JobKey JobKey = new("fx-history-backfill", "market-data");

    public async Task EnqueueAsync(CancellationToken cancellationToken)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);

        var jobDetail = JobBuilder.Create<FxHistoryBackfillJob>()
            .WithIdentity(JobKey)
            .StoreDurably()
            .Build();

        await scheduler.AddJob(jobDetail, AddJobOptions.Replacing, cancellationToken);
        await scheduler.TriggerJob(JobKey, cancellationToken: cancellationToken);
    }
}
