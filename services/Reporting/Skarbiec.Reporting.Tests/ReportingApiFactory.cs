using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Reporting.Tests;

public sealed class ReportingApiFactory(SkarbiecContainersFixture containers)
    : SkarbiecApiFactory<Program>(containers, "reporting-db")
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // A host stopped while its bus is still starting leaves the consumers attached to the class's durable queues and steals their messages.
        builder.ConfigureServices(services => services.Configure<MassTransitHostOptions>(options => options.WaitUntilStarted = true));
    }
}
