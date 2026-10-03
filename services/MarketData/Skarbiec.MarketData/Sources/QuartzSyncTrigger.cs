using Quartz;

namespace Skarbiec.MarketData.Sources;

// A fixed trigger identity makes a double click safe: a second call while the first is live conflicts and maps to AlreadyRunning.
public sealed class QuartzSyncTrigger(ISchedulerFactory schedulerFactory) : ISyncTrigger
{
    private static readonly TriggerKey ManualTriggerKey = new("manual-sync-trigger", "market-data");

    public async Task<SyncTriggerOutcome> TriggerAsync(CancellationToken cancellationToken)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);

        var trigger = TriggerBuilder.Create()
            .ForJob(PriceSyncJob.Key)
            .WithIdentity(ManualTriggerKey)
            .StartNow()
            .Build();

        try
        {
            await scheduler.ScheduleJob(trigger, cancellationToken: cancellationToken);
            return SyncTriggerOutcome.Started;
        }
        catch (ObjectAlreadyExistsException)
        {
            return SyncTriggerOutcome.AlreadyRunning;
        }
    }
}
