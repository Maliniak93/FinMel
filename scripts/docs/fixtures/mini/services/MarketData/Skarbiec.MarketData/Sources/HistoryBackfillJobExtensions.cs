namespace Skarbiec.MarketData.Sources;

public static class HistoryBackfillJobExtensions
{
    public static TBuilder AddHistoryBackfillJob<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddTransient<HistoryBackfillJob>();
        builder.Services.AddSingleton<IHistoryBackfillTrigger, QuartzHistoryBackfillTrigger>();

        return builder;
    }
}
