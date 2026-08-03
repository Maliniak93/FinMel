namespace Skarbiec.MarketData.Sources.CoinGecko;

public static class CoinGeckoSourceExtensions
{
    private static readonly Uri CoinGeckoApiBaseAddress = new("https://api.coingecko.com/api/v3/");

    // Live-verified 2026-08-03: a request without a descriptive User-Agent gets a flat 403 ("Please
    // add a descriptive User-Agent to your request") before CoinGecko even looks at the rest of the
    // request — .NET's HttpClient sends none by default, so this isn't optional.
    private const string UserAgent = "Skarbiec/1.0 (+https://github.com/; personal wealth-management app, MarketData service)";

    /// <summary>Registers the CoinGecko typed client (resilience applies automatically — see
    /// <see cref="CoinGeckoApiClient"/>'s doc comment) plus its <see cref="IPriceSource"/> implementation.</summary>
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
