using Microsoft.EntityFrameworkCore;
using Quartz;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources.MfBonds;

// Fills an empty catalog at startup instead of leaving it empty until the next cron fire.
public sealed class BondCatalogStartupTrigger(
    IServiceScopeFactory scopeFactory,
    ISchedulerFactory schedulerFactory,
    IHostApplicationLifetime lifetime,
    ILogger<BondCatalogStartupTrigger> logger) : BackgroundService
{
    // A fixed identity, so two instances starting together schedule one run, not two.
    private static readonly TriggerKey StartupTriggerKey = new("bond-catalog-startup-trigger", "market-data");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // After startup, so the Quartz hosted service has created the scheduler and stored the job.
            await WaitForApplicationStartedAsync(stoppingToken);

            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
                if (await db.BondSeries.AnyAsync(stoppingToken))
                {
                    return;
                }
            }

            var scheduler = await schedulerFactory.GetScheduler(stoppingToken);
            var trigger = TriggerBuilder.Create()
                .ForJob(BondCatalogSyncJob.Key)
                .WithIdentity(StartupTriggerKey)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(trigger, cancellationToken: stoppingToken);
            logger.LogInformation("The bond catalog is empty; a BondCatalog sync run was triggered.");
        }
        catch (ObjectAlreadyExistsException)
        {
            logger.LogInformation("A startup BondCatalog sync run is already scheduled.");
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not trigger the startup BondCatalog sync run.");
        }
    }

    private async Task WaitForApplicationStartedAsync(CancellationToken cancellationToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(cancellationToken);
    }
}
