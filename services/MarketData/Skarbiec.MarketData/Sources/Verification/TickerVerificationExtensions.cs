namespace Skarbiec.MarketData.Sources.Verification;

// Kept out of Program.cs so Program.cs never depends on IPriceSource itself.
public static class TickerVerificationExtensions
{
    public static TBuilder AddTickerVerification<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddScoped<ITickerVerifier, TickerVerifier>();

        return builder;
    }
}
