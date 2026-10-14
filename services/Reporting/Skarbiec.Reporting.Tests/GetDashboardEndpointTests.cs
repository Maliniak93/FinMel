using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Features.GetDashboard;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingApi;

namespace Skarbiec.Reporting.Tests;

public sealed class GetDashboardEndpointTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    [Fact]
    public async Task NetWorthFromUserSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var brokerageId = Guid.NewGuid();
        var bondsId = Guid.NewGuid();
        var olderDate = new DateOnly(2026, 7, 31);
        var snapshotDate = new DateOnly(2026, 8, 1);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, olderDate, 9_999m, cancellationToken);
            await db.SeedValuationLineAsync(
                userId, brokerageId, Guid.NewGuid(), olderDate, 9_999m, cancellationToken, assetClass: AssetClass.Stock);

            await db.SeedSnapshotAsync(userId, snapshotDate, 1500m, cancellationToken);
            await db.SeedValuationLineAsync(
                userId, brokerageId, Guid.NewGuid(), snapshotDate, 1000m, cancellationToken, assetClass: AssetClass.Stock);
            await db.SeedValuationLineAsync(
                userId, bondsId, Guid.NewGuid(), snapshotDate, 500m, cancellationToken, assetClass: AssetClass.Cash);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(DashboardUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1500m, body!.NetWorthPln);
        Assert.Equal(snapshotDate, body.AsOf);
        Assert.False(body.IsStale);

        Assert.Equal(2, body.ByAccount.Count);
        Assert.Equal(1000m, Assert.Single(body.ByAccount, p => p.AccountId == brokerageId).ValuePln);
        Assert.Equal(500m, Assert.Single(body.ByAccount, p => p.AccountId == bondsId).ValuePln);

        Assert.Equal(2, body.ByAssetClass.Count);
        var stock = Assert.Single(body.ByAssetClass, c => c.AssetClass == AssetClass.Stock);
        Assert.Equal(1000m, stock.ValuePln);
        Assert.Equal(66.67m, stock.Percentage);
        var cash = Assert.Single(body.ByAssetClass, c => c.AssetClass == AssetClass.Cash);
        Assert.Equal(500m, cash.ValuePln);
        Assert.Equal(33.33m, cash.Percentage);
    }

    [Fact]
    public async Task Get_SurfacesStaleFlag_WhenLatestSnapshotIsStale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var snapshotDate = new DateOnly(2026, 8, 1);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, snapshotDate, 1500m, cancellationToken, isStale: true);
            await db.SeedValuationLineAsync(
                userId, Guid.NewGuid(), Guid.NewGuid(), snapshotDate, 1500m, cancellationToken, isStale: true);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(DashboardUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.True(body!.IsStale);
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
        Assert.Empty(body.ByAccount);
    }

    [Fact]
    public async Task TenancyIsolation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var snapshotDate = new DateOnly(2026, 8, 1);

        await using (var db = CreateDbContext(userAId))
        {
            await db.SeedSnapshotAsync(userAId, snapshotDate, 5000m, cancellationToken);
            await db.SeedValuationLineAsync(
                userAId, Guid.NewGuid(), Guid.NewGuid(), snapshotDate, 5000m, cancellationToken, assetClass: AssetClass.Stock);
        }

        using var userBClient = Factory.CreateAuthenticatedClient(userBId);
        var response = await userBClient.GetAsync(DashboardUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(0m, body!.NetWorthPln);
        Assert.Null(body.AsOf);
        Assert.Empty(body.ByAccount);
        Assert.Empty(body.ByAssetClass);
    }

    [Fact]
    public async Task ByAccount_IncludesOutsideAccounts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var brokerageId = Guid.NewGuid();
        var bondsId = Guid.NewGuid();
        var snapshotDate = new DateOnly(2026, 8, 1);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, snapshotDate, 1_800m, cancellationToken);
            await db.SeedValuationLineAsync(userId, brokerageId, Guid.NewGuid(), snapshotDate, 1_000m, cancellationToken, assetClass: AssetClass.Stock);
            await db.SeedValuationLineAsync(userId, bondsId, Guid.NewGuid(), snapshotDate, 500m, cancellationToken, assetClass: AssetClass.Bond);
            await db.SeedValuationLineAsync(userId, null, Guid.NewGuid(), snapshotDate, 300m, cancellationToken, assetClass: AssetClass.Cash);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(DashboardUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, body!.ByAccount.Count);
        Assert.Equal(1_000m, Assert.Single(body.ByAccount, a => a.AccountId == brokerageId).ValuePln);
        Assert.Equal(500m, Assert.Single(body.ByAccount, a => a.AccountId == bondsId).ValuePln);
        Assert.Equal(300m, Assert.Single(body.ByAccount, a => a.AccountId == null).ValuePln);
        Assert.Equal(body.NetWorthPln, body.ByAccount.Sum(a => a.ValuePln));
    }

    [Fact]
    public async Task ByAccount_CashSplit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var emptyOfCashId = Guid.NewGuid();
        var snapshotDate = new DateOnly(2026, 8, 1);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, snapshotDate, 2_000m, cancellationToken);
            await db.SeedValuationLineAsync(userId, accountId, Guid.NewGuid(), snapshotDate, 1_000m, cancellationToken, assetClass: AssetClass.Cash);
            await db.SeedValuationLineAsync(userId, accountId, Guid.NewGuid(), snapshotDate, 500m, cancellationToken, assetClass: AssetClass.Etf);
            await db.SeedValuationLineAsync(userId, emptyOfCashId, Guid.NewGuid(), snapshotDate, 500m, cancellationToken, assetClass: AssetClass.Stock);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(DashboardUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var split = Assert.Single(body!.ByAccount, a => a.AccountId == accountId);
        Assert.Equal(1_500m, split.ValuePln);
        Assert.Equal(1_000m, split.CashPln);
        Assert.Equal(0m, Assert.Single(body.ByAccount, a => a.AccountId == emptyOfCashId).CashPln);
    }

    [Fact]
    public async Task ByAccountCarriesPercentage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var largeId = Guid.NewGuid();
        var smallId = Guid.NewGuid();
        var snapshotDate = new DateOnly(2026, 8, 1);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, snapshotDate, 1000m, cancellationToken);
            await db.SeedValuationLineAsync(userId, largeId, Guid.NewGuid(), snapshotDate, 750m, cancellationToken);
            await db.SeedValuationLineAsync(userId, smallId, Guid.NewGuid(), snapshotDate, 250m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(DashboardUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(75.00m, Assert.Single(body!.ByAccount, p => p.AccountId == largeId).Percentage);
        Assert.Equal(25.00m, Assert.Single(body.ByAccount, p => p.AccountId == smallId).Percentage);
    }

    [Fact]
    public async Task ByAccountPercentageIsZeroWhenNetWorthIsZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var snapshotDate = new DateOnly(2026, 8, 1);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, snapshotDate, 0m, cancellationToken);
            await db.SeedValuationLineAsync(userId, Guid.NewGuid(), Guid.NewGuid(), snapshotDate, 0m, cancellationToken);
            await db.SeedValuationLineAsync(userId, Guid.NewGuid(), Guid.NewGuid(), snapshotDate, 0m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(DashboardUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0m, body!.NetWorthPln);
        Assert.Equal(2, body.ByAccount.Count);
        Assert.All(body.ByAccount, p => Assert.Equal(0m, p.Percentage));
    }

    [Fact]
    public async Task BankAccount_OwnClassAndAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();
        var snapshotDate = new DateOnly(2026, 8, 1);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, snapshotDate, 2_000m, cancellationToken);
            await db.SeedValuationLineAsync(
                userId, bankAccountId, Guid.NewGuid(), snapshotDate, 2_000m, cancellationToken, assetClass: AssetClass.BankAccount);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(DashboardUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<DashboardResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bank = Assert.Single(body!.ByAssetClass);
        Assert.Equal(AssetClass.BankAccount, bank.AssetClass);
        Assert.Equal(2_000m, bank.ValuePln);
        var account = Assert.Single(body.ByAccount);
        Assert.Equal(bankAccountId, account.AccountId);
        Assert.Equal(2_000m, account.ValuePln);
    }
}
