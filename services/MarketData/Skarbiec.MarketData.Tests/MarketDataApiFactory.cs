using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.MarketData.Sources.Verification;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

public sealed class MarketDataApiFactory(SkarbiecContainersFixture containers)
    : SkarbiecApiFactory<Program>(containers, "marketdata-db")
{
    // HTTP slice tests never call a live provider; TickerVerifierTests cover the real verifier.
    public FakeTickerVerifier TickerVerifier { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
            services.AddSingleton<ITickerVerifier>(TickerVerifier));
    }
}
