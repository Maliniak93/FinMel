using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Skarbiec.Gateway.Tests.Infrastructure;

// YARP destinations point at the test hosts' real Kestrel addresses instead of service discovery.
internal sealed class GatewayTestHost(Uri identityAddress, Uri portfolioAddress) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ReverseProxy:Clusters:identity-cluster:Destinations:destination1:Address", identityAddress.ToString());
        builder.UseSetting("ReverseProxy:Clusters:portfolio-cluster:Destinations:destination1:Address", portfolioAddress.ToString());
    }
}
