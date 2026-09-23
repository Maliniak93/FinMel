using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

namespace Skarbiec.MarketData.Sources;

public static class FxSyncJobExtensions
{
    // Same key/reasoning as PriceSyncJobExtensions.DisableBackgroundJobsConfigKey — no-ops under
    // Skarbiec.Testing's SkarbiecApiFactory so slice tests never race a live Quartz scheduler.
    private const string DisableBackgroundJobsConfigKey = "Testing:DisableBackgroundJobs";

    private const string FxSyncCronConfigKey = "FxSync:Cron";

    // Business days, after NBP table A's midday publish and well before PriceSync's 18:30.
    // appsettings.Development.json overrides this to a short interval for local demoing.
    private const string DefaultProductionCron = "0 0 13 ? * MON-FRI";

    /// <summary>
    /// Registers <see cref="FxSyncJob"/> on its cron schedule. Must be called after
    /// <see cref="PriceSyncJobExtensions.AddPriceSyncJob"/>: this adds only the job and its trigger
    /// through a second, additive <c>AddQuartz</c> call and deliberately never repeats
    /// <c>UsePersistentStore</c>/<c>UseClustering</c>/<c>AddQuartzHostedService</c> — the one scheduler
    /// that call configures (Postgres-persisted, clustered) runs this job too. No-ops under
    /// <see cref="DisableBackgroundJobsConfigKey"/>; nothing in a request path triggers it, so unlike
    /// the other two jobs it needs no NoOp trigger.
    /// </summary>
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
            q.AddJob<FxSyncJob>(FxSyncJob.Key, j => j.StoreDurably());
            q.AddTrigger(t => t
                .ForJob(FxSyncJob.Key)
                .WithIdentity("fx-sync-trigger", "market-data")
                .WithCronSchedule(cron));
        });

        return builder;
    }
}
