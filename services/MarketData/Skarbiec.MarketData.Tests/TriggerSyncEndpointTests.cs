using System.Net;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

// This host has no scheduler, so SyncTriggerSchedulingTests cover the firing and the double-click guard.
public sealed class TriggerSyncEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private const string TriggerUri = "/api/marketdata/sync/trigger";

    [Fact]
    public async Task Post_WithToken_ReturnsNoContent()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsync(TriggerUri, null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsync(TriggerUri, null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
