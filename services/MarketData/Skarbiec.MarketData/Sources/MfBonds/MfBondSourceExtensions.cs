namespace Skarbiec.MarketData.Sources.MfBonds;

public static class MfBondSourceExtensions
{
    private const string UserAgent = "Skarbiec/1.0 (personal wealth-management app, MarketData service)";

    public static TBuilder AddMfBondSource<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<MfBondSourceOptions>().Bind(builder.Configuration.GetSection(MfBondSourceOptions.SectionName));
        builder.Services.AddHttpClient<IMfBondSource, MfBondSource>(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent));

        return builder;
    }
}
