using Skarbiec.MarketData.Sources.Yahoo;

namespace Skarbiec.MarketData.Sources;

// Kept out of Program.cs so Program.cs never depends on IInstrumentSearchSource itself.
public static class InstrumentSearchExtensions
{
    public static TBuilder AddInstrumentSearch<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOptions<InstrumentSearchOptions>()
            .Bind(builder.Configuration.GetSection(InstrumentSearchOptions.SectionName))
            .Validate(
                options => options.IsValid(),
                "InstrumentSearch:Exchanges needs at least one entry, each with a ProviderCode, a unique '.'-prefixed Suffix, a Name of at most 20 characters and a 3-letter Currency.")
            .ValidateOnStart();
        builder.Services.AddScoped<IInstrumentSearchSource, YahooInstrumentSearch>();

        return builder;
    }
}
