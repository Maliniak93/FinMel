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
    /// <summary>
    /// Replaces the real, provider-calling <see cref="ITickerVerifier"/> for every test using this
    /// factory (M1.6) — HTTP slice tests must not depend on a live Stooq/CoinGecko (constraint: "no
    /// test depends on a live external provider in CI"), so AddCustomInstrument's verification step is
    /// exercised against this fake instead. Real-provider outcome mapping (Exists/DoesNotExist/
    /// Unreachable off actual Stooq/CoinGecko payloads) is covered separately by
    /// <c>TickerVerifierTests</c>, which drives the real <c>TickerVerifier</c> directly against the
    /// real <c>IPriceSource</c> implementations via their Fake*ApiClient fixtures.
    /// </summary>
    public FakeTickerVerifier TickerVerifier { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
            services.AddSingleton<ITickerVerifier>(TickerVerifier));
    }
}
