using Microsoft.Extensions.DependencyInjection.Extensions;
using Skarbiec.MarketData.Sources.MfBonds;

namespace Skarbiec.MarketData.Sources;

public static class StartupSyncExtensions
{
    private const string DisableBackgroundJobsConfigKey = "Testing:DisableBackgroundJobs";
    private const string EnabledConfigKey = "StartupSync:Enabled";
    private const string MinRetryIntervalConfigKey = "StartupSync:MinRetryInterval";

    private static readonly TimeSpan DefaultMinRetryInterval = TimeSpan.FromMinutes(10);

    // The state is registered even when the sync is off, because TriggerSync always reads it. Call after the three job extensions.
    public static TBuilder AddStartupSync<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddSingleton<IStartupSyncState, StartupSyncState>();

        var configuration = builder.Configuration;
        if (configuration.GetValue<bool>(DisableBackgroundJobsConfigKey) || !configuration.GetValue(EnabledConfigKey, true))
        {
            return builder;
        }

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new StartupSyncOptions(
            configuration.GetValue(MinRetryIntervalConfigKey, DefaultMinRetryInterval),
            configuration[FxSyncJobExtensions.FxSyncCronConfigKey] ?? FxSyncJobExtensions.DefaultProductionCron,
            configuration[PriceSyncJobExtensions.PriceSyncCronConfigKey] ?? PriceSyncJobExtensions.DefaultProductionCron,
            configuration[BondCatalogSyncJobExtensions.BondCatalogSyncCronConfigKey] ?? BondCatalogSyncJobExtensions.DefaultProductionCron));
        builder.Services.AddHostedService<StartupSyncService>();

        return builder;
    }
}
