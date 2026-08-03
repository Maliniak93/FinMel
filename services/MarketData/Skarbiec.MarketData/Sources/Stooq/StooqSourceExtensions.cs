namespace Skarbiec.MarketData.Sources.Stooq;

public static class StooqSourceExtensions
{
    private static readonly Uri StooqBaseAddress = new("https://stooq.com/");

    /// <summary>Registers the Stooq typed client (resilience applies automatically — see
    /// <see cref="StooqApiClient"/>'s doc comment) plus its <see cref="IPriceSource"/> implementation.</summary>
    public static TBuilder AddStooqSource<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHttpClient<IStooqApiClient, StooqApiClient>(client => client.BaseAddress = StooqBaseAddress);
        builder.Services.AddTransient<IPriceSource, StooqPriceSource>();

        return builder;
    }
}
