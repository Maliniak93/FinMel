namespace Skarbiec.MarketData.Sources.CoinGecko;

public static class CoinGeckoSourceExtensions
{
    private static readonly Uri CoinGeckoApiBaseAddress = new("https://api.coingecko.com/api/v3/");

    // Without a descriptive User-Agent CoinGecko answers a flat 403, and HttpClient sends none by default.
    private const string UserAgent = "Skarbiec/1.0 (+https://github.com/; personal wealth-management app, MarketData service)";

    public static TBuilder AddCoinGeckoSource<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHttpClient<ICoinGeckoApiClient, CoinGeckoApiClient>(client =>
        {
            client.BaseAddress = CoinGeckoApiBaseAddress;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        });
        builder.Services.AddTransient<IPriceSource, CoinGeckoPriceSource>();

        return builder;
    }
}
