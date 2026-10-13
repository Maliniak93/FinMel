using Microsoft.Extensions.Options;

namespace Skarbiec.MarketData.Sources.Nbp;

public static class NbpSourceExtensions
{
    public static TBuilder AddNbpSources<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<NbpSourceOptions>().Bind(builder.Configuration.GetSection(NbpSourceOptions.SectionName));
        builder.Services.AddHttpClient<INbpApiClient, NbpApiClient>((services, client) =>
            client.BaseAddress = services.GetRequiredService<IOptions<NbpSourceOptions>>().Value.BaseUrl);
        builder.Services.AddTransient<IFxRateSource, NbpFxRateSource>();

        return builder;
    }
}
