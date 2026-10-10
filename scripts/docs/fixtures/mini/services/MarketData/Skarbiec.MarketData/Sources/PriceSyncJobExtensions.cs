namespace Skarbiec.MarketData.Sources;

public static class PriceSyncJobExtensions
{
    private const string PriceSyncCronConfigKey = "PriceSync:Cron";

    private const string DefaultProductionCron = "0 30 18 ? * MON-FRI";

    public static TBuilder AddPriceSyncJob<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var cron = builder.Configuration[PriceSyncCronConfigKey] ?? DefaultProductionCron;

        builder.Services.AddMarketDataScheduler(connectionString, q =>
        {
            q.AddJob<PriceSyncJob>(j => j.WithIdentity(PriceSyncJob.Key).StoreDurably());
            q.AddTrigger(t => t
                .ForJob(PriceSyncJob.Key)
                .WithIdentity("price-sync-trigger", "market-data")
                .WithCronSchedule(cron));
        });

        return builder;
    }
}
