using System.Net;
using System.Net.Http.Json;
using Skarbiec.Reporting.Features.GetNetWorthHistory;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingApi;

namespace Skarbiec.Reporting.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class GetNetWorthHistoryEndpointTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    // Range boundaries are resolved against the real clock (TimeProvider.System, same as
    // production) rather than a fake one — mirrors MarketData's job tests, which never fake time
    // either. Each fact derives its expectations from "today" instead of hardcoding dates.
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Get_OneMonthRange_ExcludesPointsOlderThanOneMonth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var tooOld = Today.AddMonths(-2);
        var withinRange = Today.AddDays(-5);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, portfolioId, tooOld, 100m, cancellationToken);
            await db.SeedSnapshotAsync(userId, portfolioId, withinRange, 200m, cancellationToken);
            await db.SeedSnapshotAsync(userId, portfolioId, Today, 300m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("1M"), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1M", body!.Range);
        Assert.Equal([withinRange, Today], body.Points.Select(p => p.Date).ToArray());
        Assert.Equal(200m, body.Points.Single(p => p.Date == withinRange).NetWorthPln);
        Assert.Equal(300m, body.Points.Single(p => p.Date == Today).NetWorthPln);
    }

    [Fact]
    public async Task Get_YtdRange_StartsAtJanuaryFirstOfCurrentYear()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var lastYearEnd = new DateOnly(Today.Year - 1, 12, 31);
        var yearStart = new DateOnly(Today.Year, 1, 1);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, portfolioId, lastYearEnd, 100m, cancellationToken);
            await db.SeedSnapshotAsync(userId, portfolioId, yearStart, 200m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("YTD"), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.DoesNotContain(body!.Points, p => p.Date == lastYearEnd);
        Assert.Contains(body.Points, p => p.Date == yearStart && p.NetWorthPln == 200m);
    }

    [Fact]
    public async Task Get_MaxRange_IncludesEarliestSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var earliest = Today.AddYears(-5);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, portfolioId, earliest, 50m, cancellationToken);
            await db.SeedSnapshotAsync(userId, portfolioId, Today, 300m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("MAX"), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Equal(earliest, body!.Points.First().Date);
        Assert.Equal(50m, body.Points.First().NetWorthPln);
    }

    [Fact]
    public async Task Get_SumsAcrossPortfolios_PerDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, Guid.NewGuid(), Today, 1000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, Guid.NewGuid(), Today, 500m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("MAX"), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        var point = Assert.Single(body!.Points);
        Assert.Equal(Today, point.Date);
        Assert.Equal(1500m, point.NetWorthPln);
    }

    [Fact]
    public async Task Get_LeavesGapsUnfilled_ForDatesWithNoSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var day1 = Today.AddDays(-2);
        var day3 = Today; // day2 (Today.AddDays(-1)) deliberately has no row — e.g. a weekend the sync job skipped.

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, portfolioId, day1, 100m, cancellationToken);
            await db.SeedSnapshotAsync(userId, portfolioId, day3, 110m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("MAX"), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Equal(2, body!.Points.Count);
        Assert.DoesNotContain(body.Points, p => p.Date == Today.AddDays(-1));
    }

    [Fact]
    public async Task Get_WithPortfolioIdFilter_ReturnsOnlyThatPortfoliosSeries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var retirementId = Guid.NewGuid();
        var savingsId = Guid.NewGuid();

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, retirementId, Today, 1000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, savingsId, Today, 500m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("MAX", retirementId), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        var point = Assert.Single(body!.Points);
        Assert.Equal(1000m, point.NetWorthPln);
    }

    [Fact]
    public async Task Get_WithInvalidRange_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(NetWorthHistoryUri("3W"), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithOmittedRange_DefaultsToOneYear()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(NetWorthHistoryBaseUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1Y", body!.Range);
    }

    [Fact]
    public async Task Get_NeverIncludesAnotherUsersSnapshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();

        await using (var db = CreateDbContext(userAId))
        {
            await db.SeedSnapshotAsync(userAId, Guid.NewGuid(), Today, 5000m, cancellationToken);
        }

        using var userBClient = Factory.CreateAuthenticatedClient(userBId);
        var response = await userBClient.GetAsync(NetWorthHistoryUri("MAX"), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Empty(body!.Points);
    }
}
