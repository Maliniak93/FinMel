using System.Net;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

// Derives straight from ServiceEndpointTests rather than a MarketData-specific base: this is the
// service's only host-backed test so far. Extract a MarketDataEndpointTests base once a second one
// needs the same Factory.
[Collection(TestingDefaults.CollectionName)]
public sealed class HealthCheckTests(SkarbiecContainersFixture containers) : ServiceEndpointTests<Program>
{
    protected override MarketDataApiFactory Factory { get; } = new(containers);

    [Fact]
    public async Task Ready_ReturnsHealthy()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
