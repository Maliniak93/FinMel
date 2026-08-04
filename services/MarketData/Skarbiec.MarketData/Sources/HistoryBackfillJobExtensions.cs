using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Skarbiec.MarketData.Sources;

public static class HistoryBackfillJobExtensions
{
    // Same key/reasoning as PriceSyncJobExtensions.DisableBackgroundJobsConfigKey — no-ops under
    // Skarbiec.Testing's SkarbiecApiFactory so slice tests never race a live Quartz scheduler.
    private const string DisableBackgroundJobsConfigKey = "Testing:DisableBackgroundJobs";

    /// <summary>
    /// Registers <see cref="HistoryBackfillJob"/> and <see cref="IHistoryBackfillTrigger"/> for DI
    /// resolution. Must be called after <see cref="PriceSyncJobExtensions.AddPriceSyncJob"/> (or
    /// another <c>AddQuartz</c> call) in the same builder — <see cref="QuartzHistoryBackfillTrigger"/>
    /// only needs <c>ISchedulerFactory</c>, which that call is what registers; this method
    /// deliberately doesn't call <c>AddQuartz</c>/<c>UsePersistentStore</c> a second time to avoid
    /// reconfiguring the one scheduler both jobs share.
    /// </summary>
    public static TBuilder AddHistoryBackfillJob<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        if (builder.Configuration.GetValue<bool>(DisableBackgroundJobsConfigKey))
        {
            return builder;
        }

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddTransient<HistoryBackfillJob>();
        builder.Services.AddSingleton<IHistoryBackfillTrigger, QuartzHistoryBackfillTrigger>();

        return builder;
    }
}
