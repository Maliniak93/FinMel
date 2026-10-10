namespace Skarbiec.MarketData.Sources.Yahoo;

public static class YahooSourceExtensions
{
    private static readonly Uri YahooApiBaseAddress = new("https://yahoo.fixture.test/");

    public static TBuilder AddYahooSource<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHttpClient<IYahooApiClient, YahooApiClient>(client => client.BaseAddress = YahooApiBaseAddress);
        builder.Services.AddTransient<IPriceSource, YahooPriceSource>();

        return builder;
    }
}
