using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz;

namespace Skarbiec.MarketData.Sources;

public static class PriceSyncJobExtensions
{
    // Mirrors Skarbiec.Testing's TestingDefaults.DisableBackgroundJobsConfigKey ("Testing:DisableBackgroundJobs"),
    // kept as a literal rather than a project reference so this service never depends on the
    // test-infrastructure project. SkarbiecApiFactory sets this to "true" on every
    // WebApplicationFactory-based test host so slice tests never race a live Quartz job.
    private const string DisableBackgroundJobsConfigKey = "Testing:DisableBackgroundJobs";

    private const string PriceSyncCronConfigKey = "PriceSync:Cron";

    // Business days, after GPW's ~17:00 CET close and well after NBP table A's midday publish
    // (diagrams/price-sync-sequence.mermaid) — gives Stooq's EOD data time to settle before sync.
    // appsettings.Development.json overrides this to a short interval for local demoing.
    private const string DefaultProductionCron = "0 30 18 ? * MON-FRI";

    // Schema-qualified to match MarketDataDbContext's modelBuilder.AddQuartz(b => b.UsePostgreSql())
    // default (schema "quartz", prefix "qrtz_").
    private const string QuartzTablePrefix = "quartz.qrtz_";

    /// <summary>
    /// Registers <see cref="PriceSyncJob"/> on a Postgres-backed, clustered persistent store — the
    /// schedule and in-flight run state survive a service restart, and <c>DisallowConcurrentExecution</c>
    /// plus clustering keep two live instances from ever running the same fire concurrently (T2.6 AC).
    /// No-ops under <see cref="DisableBackgroundJobsConfigKey"/> so slice tests never race this job.
    /// </summary>
    public static TBuilder AddPriceSyncJob<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        if (builder.Configuration.GetValue<bool>(DisableBackgroundJobsConfigKey))
        {
            return builder;
        }

        var connectionString = builder.Configuration.GetConnectionString("marketdata-db")
            ?? throw new InvalidOperationException("Missing connection string 'marketdata-db'.");
        var cron = builder.Configuration[PriceSyncCronConfigKey] ?? DefaultProductionCron;

        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.Services.AddQuartz(q =>
        {
            q.SchedulerId = "AUTO"; // unique per instance — required for UseClustering() below.

            q.UsePersistentStore(store =>
            {
                store.UsePostgres(c =>
                {
                    c.ConnectionString = connectionString;
                    c.TablePrefix = QuartzTablePrefix;
                });
                store.UseSystemTextJsonSerializer();
                store.UseClustering();
            });

            q.AddJob<PriceSyncJob>(PriceSyncJob.Key, j => j.StoreDurably());
            q.AddTrigger(t => t
                .ForJob(PriceSyncJob.Key)
                .WithIdentity("price-sync-trigger", "market-data")
                .WithCronSchedule(cron));
        });

        builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

        return builder;
    }
}
