namespace Skarbiec.MarketData.Sources.Verification;

/// <summary>Registers <see cref="ITickerVerifier"/> — kept out of Program.cs directly, mirroring every
/// <c>Add*Source</c> extension (e.g. CoinGecko's), so Program.cs never depends on <see cref="IPriceSource"/>
/// itself and stays outside the guardrail in <c>ArchitectureTests.OnlySourcesNamespace_DependsOn_PriceSourceAbstractions</c>.</summary>
public static class TickerVerificationExtensions
{
    public static TBuilder AddTickerVerification<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddScoped<ITickerVerifier, TickerVerifier>();

        return builder;
    }
}
