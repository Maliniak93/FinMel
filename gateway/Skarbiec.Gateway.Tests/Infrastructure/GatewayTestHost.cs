using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Skarbiec.Gateway.Tests.Infrastructure;

/// <summary>
/// The Gateway under test, with its two YARP cluster destinations overridden to point at the
/// real Kestrel addresses <see cref="IdentityTestHost"/>/<see cref="PortfolioTestHost"/> listen on
/// instead of the "https://identity-service"/"https://portfolio-service" Aspire service-discovery
/// addresses from appsettings.json.
/// </summary>
internal sealed class GatewayTestHost(Uri identityAddress, Uri portfolioAddress) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ReverseProxy:Clusters:identity-cluster:Destinations:destination1:Address", identityAddress.ToString());
        builder.UseSetting("ReverseProxy:Clusters:portfolio-cluster:Destinations:destination1:Address", portfolioAddress.ToString());
    }
}
