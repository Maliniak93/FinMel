namespace Skarbiec.MarketData.Sources.StooqApi;

public static class StooqApiSourceExtensions
{
    public static TBuilder AddStooqApiSource<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<StooqApiSourceOptions>().BindConfiguration(StooqApiSourceOptions.SectionName);
        builder.Services.AddHttpClient<IStooqApiClient, StooqApiClient>((sp, client) =>
            client.BaseAddress = sp.GetRequiredService<IOptions<StooqApiSourceOptions>>().Value.BaseUrl);
        builder.Services.AddTransient<IPriceSource, StooqApiPriceSource>();

        return builder;
    }
}
