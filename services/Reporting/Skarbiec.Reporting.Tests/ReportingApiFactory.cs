using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Reporting.Tests;

public sealed class ReportingApiFactory(SkarbiecContainersFixture containers, IPriceQuoteClient? priceQuoteClient = null)
    : SkarbiecApiFactory<Program>(containers, "reporting-db")
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        if (priceQuoteClient is not null)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPriceQuoteClient>();
                services.AddSingleton(priceQuoteClient);
            });
        }

        // A host stopped while its bus is still starting leaves the consumers attached to the class's durable queues and steals their messages.
        builder.ConfigureServices(services => services.Configure<MassTransitHostOptions>(options => options.WaitUntilStarted = true));
    }
}
