using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

namespace Skarbiec.MarketData.Sources;

public static class PriceSyncJobExtensions
{
    // A literal, not a reference to Skarbiec.Testing, so the service never depends on test infrastructure.
    private const string DisableBackgroundJobsConfigKey = "Testing:DisableBackgroundJobs";

    private const string PriceSyncCronConfigKey = "PriceSync:Cron";

    // Business days, after the GPW close and NBP's midday publish, giving Stooq's EOD data time to settle.
    private const string DefaultProductionCron = "0 30 18 ? * MON-FRI";

    // Postgres-backed and clustered, so the schedule survives a restart and two instances never run the same fire.
    public static TBuilder AddPriceSyncJob<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        if (builder.Configuration.GetValue<bool>(DisableBackgroundJobsConfigKey))
        {
            // TriggerSync still resolves ISyncTrigger in slice tests with no scheduler.
            builder.Services.AddSingleton<ISyncTrigger, NoOpSyncTrigger>();
            return builder;
        }

        var connectionString = builder.Configuration.GetConnectionString("marketdata-db")
            ?? throw new InvalidOperationException("Missing connection string 'marketdata-db'.");
        var cron = builder.Configuration[PriceSyncCronConfigKey] ?? DefaultProductionCron;

        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.Services.AddMarketDataScheduler(connectionString, q =>
        {
            q.AddJob<PriceSyncJob>(j => j.WithIdentity(PriceSyncJob.Key).StoreDurably());
            q.AddTrigger(t => t
                .ForJob(PriceSyncJob.Key)
                .WithIdentity("price-sync-trigger", "market-data")
                .WithCronSchedule(cron));
        });

        // Never under DisableBackgroundJobs: with no scheduler the check would fail readiness in every slice test.
        builder.Services.AddHealthChecks().AddQuartz();
        builder.Services.AddSingleton<ISyncTrigger, QuartzSyncTrigger>();

        return builder;
    }
}
