namespace Skarbiec.MarketData.Sources.Nbp;

public static class NbpSourceExtensions
{
    private static readonly Uri NbpApiBaseAddress = new("https://api.nbp.pl/api/");

    public static TBuilder AddNbpSources<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHttpClient<INbpApiClient, NbpApiClient>(client => client.BaseAddress = NbpApiBaseAddress);
        builder.Services.AddTransient<IFxRateSource, NbpFxRateSource>();

        return builder;
    }
}
