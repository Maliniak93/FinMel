using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Portfolio.MarketData;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class PortfolioApiFactory(SkarbiecContainersFixture containers)
    : SkarbiecApiFactory<Program>(containers, "portfolio-db")
{
    /// <summary>
    /// Replaces the real MarketData-calling <see cref="IInstrumentLookupClient"/> for every test
    /// using this factory (T2.9) — no MarketData Testcontainer runs alongside Portfolio's, so
    /// AddAsset/UpdateAsset's market-mode path is exercised against this fake instead.
    /// </summary>
    public FakeInstrumentLookupClient InstrumentLookupClient { get; } = new();

    /// <summary>
    /// Replaces the real MarketData-calling <see cref="IFxRateLookupClient"/> the same way
    /// (transactions-pln-value-and-fee-removal): every transaction write resolves its PLN rate
    /// against this fake, which also records each call.
    /// </summary>
    public FakeFxRateLookupClient FxRateLookupClient { get; } = new();

    /// <summary>
    /// The host's <see cref="TimeProvider"/>: the real clock unless a fact pins it (term deposits'
    /// Active/Due status is derived from the Europe/Warsaw date of "now").
    /// </summary>
    public AdjustableTimeProvider Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IInstrumentLookupClient>(InstrumentLookupClient);
            services.AddSingleton<IFxRateLookupClient>(FxRateLookupClient);
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}
