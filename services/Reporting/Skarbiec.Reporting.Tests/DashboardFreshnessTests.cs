using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Features.GetDashboard;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingApi;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingConsumers;

namespace Skarbiec.Reporting.Tests;

// The dashboard is called directly; each step waits for the previous consume, since the two event types sit on separate queues.
public sealed class DashboardFreshnessTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    [Fact]
    public async Task AddCash_ThenGetDashboard_ShowsNewNetWorthWithoutSync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.WaitUntilReadyAsync(cancellationToken);

        await Bus.PublishCashPositionAsync(userId, accountId, 500m, cancellationToken);

        using var response = await client.GetDashboardWhenAsync(d => d.NetWorthPln == 500m, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(500m, body!.NetWorthPln);
        Assert.False(body.IsStale);
        Assert.Equal(accountId, Assert.Single(body.ByAccount).AccountId);
        Assert.Equal(AssetClass.Cash, Assert.Single(body.ByAssetClass).AssetClass);
    }

    [Fact]
    public async Task AddCash_NeverAppearsOnAnotherUsersDashboard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();

        using var userAClient = Factory.CreateAuthenticatedClient(userAId);
        await userAClient.WaitUntilReadyAsync(cancellationToken);

        await Bus.PublishCashPositionAsync(userAId, Guid.NewGuid(), 500m, cancellationToken);

        // User A's dashboard reaching 500 signals the event was consumed, so asserting B cannot pass vacuously.
        using var userAResponse = await userAClient.GetDashboardWhenAsync(d => d.NetWorthPln == 500m, cancellationToken);
        var userABody = await userAResponse.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);
        Assert.Equal(500m, userABody!.NetWorthPln);

        using var userBClient = Factory.CreateAuthenticatedClient(userBId);
        using var userBResponse = await userBClient.GetAsync(DashboardUri, cancellationToken);
        var userBBody = await userBResponse.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, userBResponse.StatusCode);
        Assert.Equal(0m, userBBody!.NetWorthPln);
        Assert.Null(userBBody.AsOf);
        Assert.Empty(userBBody.ByAccount);
        Assert.Empty(userBBody.ByAssetClass);
    }

    // The intermediate 500 is asserted too, or an archive that dropped nothing would pass.
    [Fact]
    public async Task ArchivedAsset_LeavesNetWorth_AndRestoreBringsItBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var archivedAccountId = Guid.NewGuid();
        var archivedAssetId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.WaitUntilReadyAsync(cancellationToken);
        await Bus.PublishCashPositionAsync(userId, archivedAccountId, 1_000m, cancellationToken, assetId: archivedAssetId);
        await Bus.PublishCashPositionAsync(userId, Guid.NewGuid(), 500m, cancellationToken);
        using (var both = await client.GetDashboardWhenAsync(d => d.NetWorthPln == 1_500m, cancellationToken))
        {
            Assert.Equal(1_500m, (await both.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken))!.NetWorthPln);
        }

        await Bus.PublishCashPositionAsync(
            userId, archivedAccountId, 1_000m, cancellationToken, assetId: archivedAssetId, version: 1, isArchived: true);
        using (var archived = await client.GetDashboardWhenAsync(d => d.NetWorthPln == 500m, cancellationToken))
        {
            Assert.Equal(500m, (await archived.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken))!.NetWorthPln);
        }

        await Bus.PublishCashPositionAsync(
            userId, archivedAccountId, 1_000m, cancellationToken, assetId: archivedAssetId, version: 2, isArchived: false);
        using var restored = await client.GetDashboardWhenAsync(d => d.NetWorthPln == 1_500m, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }
}
