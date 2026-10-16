using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class PortfolioApiFactory(SkarbiecContainersFixture containers)
    : SkarbiecApiFactory<Program>(containers, "portfolio-db")
{
    public FakeInstrumentLookupClient InstrumentLookupClient { get; } = new();

    public FakeFxRateLookupClient FxRateLookupClient { get; } = new();

    public FakeBondRateLookupClient BondRateLookupClient { get; } = new();

    public FakeInstrumentQuoteLookupClient InstrumentQuoteLookupClient { get; } = new();

    public AdjustableTimeProvider Clock { get; } = new();

    internal DbCommandCounter Commands { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IInstrumentLookupClient>(InstrumentLookupClient);
            services.AddSingleton<IFxRateLookupClient>(FxRateLookupClient);
            services.AddSingleton<IBondRateLookupClient>(BondRateLookupClient);
            services.AddSingleton<IInstrumentQuoteLookupClient>(InstrumentQuoteLookupClient);
            services.AddSingleton<TimeProvider>(Clock);
            services.ConfigureDbContext<PortfolioDbContext>(options => options.AddInterceptors(Commands));
        });
    }
}
