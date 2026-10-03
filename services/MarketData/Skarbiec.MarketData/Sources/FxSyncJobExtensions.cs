using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

namespace Skarbiec.MarketData.Sources;

public static class FxSyncJobExtensions
{
    private const string DisableBackgroundJobsConfigKey = "Testing:DisableBackgroundJobs";

    private const string FxSyncCronConfigKey = "FxSync:Cron";

    // Business days, after NBP table A's midday publish and well before PriceSync's 18:30.
    private const string DefaultProductionCron = "0 0 13 ? * MON-FRI";

    // Call after AddPriceSyncJob: this adds only the job and its trigger, and the scheduler that call configures runs it.
    public static TBuilder AddFxSyncJob<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        if (builder.Configuration.GetValue<bool>(DisableBackgroundJobsConfigKey))
        {
            return builder;
        }

        var cron = builder.Configuration[FxSyncCronConfigKey] ?? DefaultProductionCron;

        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.Services.AddQuartz(q =>
        {
            q.AddJob<FxSyncJob>(j => j.WithIdentity(FxSyncJob.Key).StoreDurably());
            q.AddTrigger(t => t
                .ForJob(FxSyncJob.Key)
                .WithIdentity("fx-sync-trigger", "market-data")
                .WithCronSchedule(cron));
        });

        return builder;
    }
}
