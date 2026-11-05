using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.GetFxRate;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.Verification;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

public sealed class MarketDataApiFactory(SkarbiecContainersFixture containers)
    : SkarbiecApiFactory<Program>(containers, "marketdata-db")
{
    // HTTP slice tests never call a live provider; TickerVerifierTests cover the real verifier.
    public FakeTickerVerifier TickerVerifier { get; } = new();

    public FakeInstrumentSearchSource InstrumentSearch { get; } = new();

    public FakeHistoryBackfillTrigger BackfillTrigger { get; } = new();

    public FakeFxHistoryBackfillTrigger FxBackfillTrigger { get; } = new();

    public IFxRateSource FxRateSource { get; set; } = new NoOpFxRateSource();

    public TimeSpan FxReadThroughTimeout { get; set; } = GetFxRateHandler.DefaultSourceTimeout;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddScoped(sp => new GetFxRateHandler(
                sp.GetRequiredService<MarketDataDbContext>(),
                sp.GetRequiredService<IFxRateSource>(),
                sp.GetRequiredService<ILogger<GetFxRateHandler>>(),
                FxReadThroughTimeout));
            services.AddSingleton<ITickerVerifier>(TickerVerifier);
            services.AddSingleton<IFxRateSource>(_ => FxRateSource);
            services.AddSingleton<IInstrumentSearchSource>(InstrumentSearch);
            services.AddSingleton<IHistoryBackfillTrigger>(BackfillTrigger);
            services.AddSingleton<IFxHistoryBackfillTrigger>(FxBackfillTrigger);
        });
    }
}
