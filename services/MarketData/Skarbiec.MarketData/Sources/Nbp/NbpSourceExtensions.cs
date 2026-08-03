namespace Skarbiec.MarketData.Sources.Nbp;

public static class NbpSourceExtensions
{
    private static readonly Uri NbpApiBaseAddress = new("https://api.nbp.pl/api/");

    /// <summary>Registers the NBP typed client (resilience applies automatically — see
    /// <see cref="NbpApiClient"/>'s doc comment) plus its <see cref="IPriceSource"/>/
    /// <see cref="IFxRateSource"/> implementations.</summary>
    public static TBuilder AddNbpSources<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHttpClient<INbpApiClient, NbpApiClient>(client => client.BaseAddress = NbpApiBaseAddress);
        builder.Services.AddTransient<IPriceSource, NbpPriceSource>();
        builder.Services.AddTransient<IFxRateSource, NbpFxRateSource>();

        return builder;
    }
}
