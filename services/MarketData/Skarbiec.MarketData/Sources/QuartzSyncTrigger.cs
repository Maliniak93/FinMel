using Quartz;

namespace Skarbiec.MarketData.Sources;

/// <summary>
/// <see cref="ISyncTrigger"/> over Quartz: schedules a one-shot, immediate-fire trigger against the
/// already-registered durable <see cref="PriceSyncJob"/> (T2.6's <c>AddPriceSyncJob</c>), under a
/// fixed identity instead of a random one. That fixed identity is what makes a double-click safe —
/// Quartz's job store enforces trigger names unique per group, so a second call while the first
/// trigger (and the run it kicked off — <see cref="PriceSyncJob"/> is <c>[DisallowConcurrentExecution]</c>,
/// so the trigger isn't retired until its fire has actually finished executing) is still live hits a
/// unique-constraint conflict and surfaces as <see cref="ObjectAlreadyExistsException"/>, mapped here
/// to <see cref="SyncTriggerOutcome.AlreadyRunning"/> instead of silently queuing a second run.
/// </summary>
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
            await scheduler.ScheduleJob(trigger, cancellationToken);
            return SyncTriggerOutcome.Started;
        }
        catch (ObjectAlreadyExistsException)
        {
            return SyncTriggerOutcome.AlreadyRunning;
        }
    }
}
