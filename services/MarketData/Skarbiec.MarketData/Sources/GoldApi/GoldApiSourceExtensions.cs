using Microsoft.Extensions.Options;

namespace Skarbiec.MarketData.Sources.GoldApi;

public static class GoldApiSourceExtensions
{
    public static TBuilder AddGoldApiSource<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<GoldApiSourceOptions>().Bind(builder.Configuration.GetSection(GoldApiSourceOptions.SectionName));
        builder.Services.AddHttpClient<IGoldApiClient, GoldApiClient>((services, client) =>
            client.BaseAddress = services.GetRequiredService<IOptions<GoldApiSourceOptions>>().Value.BaseUrl);
        builder.Services.AddTransient<IPriceSource, GoldApiPriceSource>();

        return builder;
    }
}
