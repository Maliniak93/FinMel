using System.Net;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// HTTP-level behavior of Features/TriggerSync (T2.14): auth gating and the happy path against
/// <see cref="Sources.NoOpSyncTrigger"/> (this host runs under <c>Testing:DisableBackgroundJobs</c>,
/// so there's no live Quartz scheduler to actually fire — that mechanism, including the double-click
/// guard, is covered against a real one in <see cref="SyncTriggerSchedulingTests"/>).
/// </summary>
[Collection(TestingDefaults.CollectionName)]
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
