using System.Net;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Http;

namespace Skarbiec.MarketData.Tests;

public sealed class HealthCheckTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Ready_ReturnsHealthy()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetReadyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
