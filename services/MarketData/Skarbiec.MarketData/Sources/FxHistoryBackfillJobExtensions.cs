using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Skarbiec.MarketData.Sources;

public static class FxHistoryBackfillJobExtensions
{
    private const string DisableBackgroundJobsConfigKey = "Testing:DisableBackgroundJobs";
    private const string MinHistoryDaysConfigKey = "FxSync:MinHistoryDays";
    private const string HistoryMarginDaysConfigKey = "FxSync:HistoryMarginDays";

    // Call after AddPriceSyncJob, which registers the ISchedulerFactory this needs; AddQuartz is never repeated.
    public static TBuilder AddFxHistoryBackfillJob<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var defaults = new FxHistoryOptions();
        builder.Services.AddSingleton(new FxHistoryOptions(
            builder.Configuration.GetValue(MinHistoryDaysConfigKey, defaults.MinHistoryDays),
            builder.Configuration.GetValue(HistoryMarginDaysConfigKey, defaults.HistoryMarginDays)));

        if (builder.Configuration.GetValue<bool>(DisableBackgroundJobsConfigKey))
        {
            builder.Services.AddSingleton<IFxHistoryBackfillTrigger, NoOpFxHistoryBackfillTrigger>();
            return builder;
        }

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddTransient<FxHistoryBackfillJob>();
        builder.Services.AddSingleton<IFxHistoryBackfillTrigger, QuartzFxHistoryBackfillTrigger>();

        return builder;
    }
}
