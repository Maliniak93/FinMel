using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

namespace Skarbiec.MarketData.Sources.MfBonds;

public static class BondCatalogSyncJobExtensions
{
    private const string DisableBackgroundJobsConfigKey = "Testing:DisableBackgroundJobs";

    private const string BondCatalogSyncCronConfigKey = "BondCatalogSync:Cron";

    // Daily, early: MF replaces the file roughly monthly, and a new month's offer starts on its first day.
    private const string DefaultProductionCron = "0 0 7 * * ?";

    // Call after AddPriceSyncJob, whose scheduler runs this job; the IMfBondSource comes from AddMfBondSource.
    public static TBuilder AddBondCatalogSyncJob<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        if (builder.Configuration.GetValue<bool>(DisableBackgroundJobsConfigKey))
        {
            return builder;
        }

        var cron = builder.Configuration[BondCatalogSyncCronConfigKey] ?? DefaultProductionCron;

        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.Services.AddQuartz(q =>
        {
            q.AddJob<BondCatalogSyncJob>(j => j.WithIdentity(BondCatalogSyncJob.Key).StoreDurably());
            q.AddTrigger(t => t
                .ForJob(BondCatalogSyncJob.Key)
                .WithIdentity("bond-catalog-sync-trigger", "market-data")
                .WithCronSchedule(cron));
        });

        builder.Services.AddHostedService<BondCatalogStartupTrigger>();

        return builder;
    }
}
