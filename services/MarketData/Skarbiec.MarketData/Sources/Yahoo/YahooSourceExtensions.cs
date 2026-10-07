namespace Skarbiec.MarketData.Sources.Yahoo;

public static class YahooSourceExtensions
{
    private static readonly Uri YahooApiBaseAddress = new("https://query1.finance.yahoo.com/");

    // Yahoo rejects requests without a browser-like User-Agent.
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

    public static TBuilder AddYahooSource<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHttpClient<IYahooApiClient, YahooApiClient>(client =>
        {
            client.BaseAddress = YahooApiBaseAddress;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        });
        builder.Services.AddTransient<IPriceSource, YahooPriceSource>();

        return builder;
    }
}
