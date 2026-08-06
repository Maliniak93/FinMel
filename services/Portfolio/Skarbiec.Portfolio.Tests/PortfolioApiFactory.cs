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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
            services.AddSingleton<IInstrumentLookupClient>(InstrumentLookupClient));
    }
}
