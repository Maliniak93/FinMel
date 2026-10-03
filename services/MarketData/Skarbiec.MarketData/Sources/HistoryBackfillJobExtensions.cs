using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Skarbiec.MarketData.Sources;

public static class HistoryBackfillJobExtensions
{
    private const string DisableBackgroundJobsConfigKey = "Testing:DisableBackgroundJobs";

    // Call after AddPriceSyncJob, which registers the ISchedulerFactory this needs; AddQuartz is never repeated.
    public static TBuilder AddHistoryBackfillJob<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        if (builder.Configuration.GetValue<bool>(DisableBackgroundJobsConfigKey))
        {
            // AddCustomInstrument still resolves IHistoryBackfillTrigger in slice tests with no scheduler.
            builder.Services.AddSingleton<IHistoryBackfillTrigger, NoOpHistoryBackfillTrigger>();
            return builder;
        }

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddTransient<HistoryBackfillJob>();
        builder.Services.AddSingleton<IHistoryBackfillTrigger, QuartzHistoryBackfillTrigger>();

        return builder;
    }
}
