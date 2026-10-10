namespace Skarbiec.MarketData.Sources.GoldApi;

public static class GoldApiSourceExtensions
{
    public static TBuilder AddGoldApiSource<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<GoldApiSourceOptions>().BindConfiguration(GoldApiSourceOptions.SectionName);
        builder.Services.AddHttpClient<IGoldApiClient, GoldApiClient>((sp, client) =>
            client.BaseAddress = sp.GetRequiredService<IOptions<GoldApiSourceOptions>>().Value.BaseUrl);
        builder.Services.AddTransient<IPriceSource, GoldApiPriceSource>();

        return builder;
    }
}
