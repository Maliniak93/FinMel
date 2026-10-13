using Microsoft.Extensions.Options;

namespace Skarbiec.MarketData.Sources.Yahoo;

public static class YahooSourceExtensions
{
    // Yahoo rejects requests without a browser-like User-Agent.
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

    public static TBuilder AddYahooSource<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<YahooSourceOptions>().Bind(builder.Configuration.GetSection(YahooSourceOptions.SectionName));
        builder.Services.AddHttpClient<IYahooApiClient, YahooApiClient>((services, client) =>
        {
            client.BaseAddress = services.GetRequiredService<IOptions<YahooSourceOptions>>().Value.BaseUrl;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        });
        builder.Services.AddTransient<IPriceSource, YahooPriceSource>();

        return builder;
    }
}
