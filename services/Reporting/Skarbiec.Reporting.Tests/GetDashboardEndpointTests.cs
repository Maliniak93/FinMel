using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Features.GetDashboard;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Reporting.Valuation;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingApi;

namespace Skarbiec.Reporting.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class GetDashboardEndpointTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    [Fact]
    public async Task Get_LatestSnapshotPerPortfolio_AggregatesNetWorthAndBreakdowns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var retirementId = Guid.NewGuid();
        var savingsId = Guid.NewGuid();
        var snapshotDate = new DateOnly(2026, 8, 1);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(
                userId, retirementId, snapshotDate, 1000m, cancellationToken,
                breakdown: [new AssetClassBreakdownEntry(AssetClass.Stock, 1000m)]);
            await db.SeedSnapshotAsync(
                userId, savingsId, snapshotDate, 500m, cancellationToken,
                breakdown: [new AssetClassBreakdownEntry(AssetClass.Cash, 500m)]);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(DashboardUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1500m, body!.NetWorthPln);
        Assert.Equal(snapshotDate, body.AsOf);
        Assert.False(body.IsStale);

        Assert.Equal(2, body.ByPortfolio.Count);
        Assert.Equal(1000m, Assert.Single(body.ByPortfolio, p => p.PortfolioId == retirementId).ValuePln);
        Assert.Equal(500m, Assert.Single(body.ByPortfolio, p => p.PortfolioId == savingsId).ValuePln);

        Assert.Equal(2, body.ByAssetClass.Count);
        var stock = Assert.Single(body.ByAssetClass, c => c.AssetClass == AssetClass.Stock);
        Assert.Equal(1000m, stock.ValuePln);
        Assert.Equal(66.67m, stock.Percentage);
        var cash = Assert.Single(body.ByAssetClass, c => c.AssetClass == AssetClass.Cash);
        Assert.Equal(500m, cash.ValuePln);
        Assert.Equal(33.33m, cash.Percentage);
    }

    [Fact]
    public async Task Get_UsesLatestDatePerPortfolio_NotOneGlobalLatestDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var caughtUpId = Guid.NewGuid();
        var laggingId = Guid.NewGuid();
        var olderDate = new DateOnly(2026, 8, 1);
        var newerDate = new DateOnly(2026, 8, 2);

        // laggingId never got a row for newerDate — the DailyPricesSynced consumer (T2.11) leaves a
        // failed portfolio's last successful snapshot in place instead of overwriting it.
        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, caughtUpId, olderDate, 1000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, caughtUpId, newerDate, 1100m, cancellationToken);
            await db.SeedSnapshotAsync(userId, laggingId, olderDate, 500m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(DashboardUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(1600m, body!.NetWorthPln); // 1100 (caughtUp's newest) + 500 (lagging's only row).
        Assert.Equal(newerDate, body.AsOf);

        var caughtUp = Assert.Single(body.ByPortfolio, p => p.PortfolioId == caughtUpId);
        Assert.Equal(1100m, caughtUp.ValuePln);
        Assert.Equal(newerDate, caughtUp.SnapshotDate);

        var lagging = Assert.Single(body.ByPortfolio, p => p.PortfolioId == laggingId);
        Assert.Equal(500m, lagging.ValuePln);
        Assert.Equal(olderDate, lagging.SnapshotDate);
    }

    [Fact]
    public async Task Get_WithPortfolioIdFilter_ReturnsOnlyThatPortfolio()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var retirementId = Guid.NewGuid();
        var savingsId = Guid.NewGuid();
        var snapshotDate = new DateOnly(2026, 8, 1);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, retirementId, snapshotDate, 1000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, savingsId, snapshotDate, 500m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(DashboardUri_ForPortfolio(retirementId), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(1000m, body!.NetWorthPln);
        Assert.Equal(retirementId, Assert.Single(body.ByPortfolio).PortfolioId);
    }

    [Fact]
    public async Task Get_SurfacesStaleFlag_WhenAnyPortfolioSnapshotIsStale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var freshId = Guid.NewGuid();
        var staleId = Guid.NewGuid();
        var snapshotDate = new DateOnly(2026, 8, 1);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, freshId, snapshotDate, 1000m, cancellationToken, isStale: false);
            await db.SeedSnapshotAsync(userId, staleId, snapshotDate, 500m, cancellationToken, isStale: true);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(DashboardUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.True(body!.IsStale);
        Assert.False(Assert.Single(body.ByPortfolio, p => p.PortfolioId == freshId).IsStale);
        Assert.True(Assert.Single(body.ByPortfolio, p => p.PortfolioId == staleId).IsStale);
    }

    [Fact]
    public async Task Get_WithNoSnapshots_ReturnsZeroAndEmptyBreakdowns()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(DashboardUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0m, body!.NetWorthPln);
        Assert.Null(body.AsOf);
        Assert.False(body.IsStale);
        Assert.Empty(body.ByAssetClass);
        Assert.Empty(body.ByPortfolio);
    }

    [Fact]
    public async Task Get_NeverIncludesAnotherUsersSnapshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();

        await using (var db = CreateDbContext(userAId))
        {
            await db.SeedSnapshotAsync(userAId, Guid.NewGuid(), new DateOnly(2026, 8, 1), 5000m, cancellationToken);
        }

        using var userBClient = Factory.CreateAuthenticatedClient(userBId);
        var response = await userBClient.GetAsync(DashboardUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(0m, body!.NetWorthPln);
        Assert.Empty(body.ByPortfolio);
    }
}
