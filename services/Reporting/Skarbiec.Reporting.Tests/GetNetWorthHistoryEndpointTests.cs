using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Features.GetNetWorthHistory;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingApi;

namespace Skarbiec.Reporting.Tests;

public sealed class GetNetWorthHistoryEndpointTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    // The real clock, as in production, so each fact derives its dates from today.
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
        var day3 = Today;

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
    public async Task ReturnsChangeBetweenFirstAndLastPoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var firstPortfolioId = Guid.NewGuid();
        var secondPortfolioId = Guid.NewGuid();

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, firstPortfolioId, Today.AddDays(-30), 600m, cancellationToken);
            await db.SeedSnapshotAsync(userId, secondPortfolioId, Today.AddDays(-30), 400m, cancellationToken);
            await db.SeedSnapshotAsync(userId, firstPortfolioId, Today.AddDays(-10), 5000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, firstPortfolioId, Today, 1250m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("1Y"), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(250.00m, body!.ChangePln);
        Assert.Equal(25.00m, body.ChangePercent);
    }

    [Fact]
    public async Task ChangeIsNullWithoutTwoPoints()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, Guid.NewGuid(), Today, 1000m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("1Y"), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Single(body!.Points);
        Assert.Null(body.ChangePln);
        Assert.Null(body.ChangePercent);
    }

    [Fact]
    public async Task ChangePercentIsNullWhenFirstPointIsZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, portfolioId, Today.AddDays(-5), 0m, cancellationToken);
            await db.SeedSnapshotAsync(userId, portfolioId, Today, 500m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("1Y"), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Equal(500.00m, body!.ChangePln);
        Assert.Null(body.ChangePercent);
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

    [Fact]
    public async Task Get_WithAssetClass_SumsThatClassAcrossPortfoliosPerDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var portfolioA = Guid.NewGuid();
        var portfolioB = Guid.NewGuid();
        var days = new[] { Today.AddDays(-20), Today.AddDays(-10), Today };
        var etfA = new[] { 100m, 150m, 200m };
        var etfB = new[] { 50m, 50m, 100m };

        await using (var db = CreateDbContext(userId))
        {
            for (var i = 0; i < days.Length; i++)
            {
                await db.SeedSnapshotAsync(userId, portfolioA, days[i], etfA[i] + 1000m, cancellationToken);
                await db.SeedSnapshotAsync(userId, portfolioB, days[i], etfB[i] + 500m, cancellationToken);
                await db.SeedValuationLineAsync(userId, portfolioA, Guid.NewGuid(), days[i], etfA[i], cancellationToken, AssetClass.Etf);
                await db.SeedValuationLineAsync(userId, portfolioA, Guid.NewGuid(), days[i], 1000m, cancellationToken, AssetClass.Cash);
                await db.SeedValuationLineAsync(userId, portfolioB, Guid.NewGuid(), days[i], etfB[i], cancellationToken, AssetClass.Etf);
                await db.SeedValuationLineAsync(userId, portfolioB, Guid.NewGuid(), days[i], 500m, cancellationToken, AssetClass.Cash);
            }
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("1Y", assetClass: AssetClass.Etf), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(days, body!.Points.Select(p => p.Date).ToArray());
        Assert.Equal([150m, 200m, 300m], body.Points.Select(p => p.NetWorthPln).ToArray());
        Assert.Equal(150m, body.ChangePln);
        Assert.Equal(100m, body.ChangePercent);
    }

    [Fact]
    public async Task Get_WithAssetClass_IsZeroBeforeTheClassIsHeld()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var jan = new DateOnly(Today.Year - 1, Today.Month, 1).AddDays(1);
        var mid = Today.AddMonths(-6);
        var last = Today;

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, portfolioId, jan, 1000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, portfolioId, mid, 1000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, portfolioId, last, 1700m, cancellationToken);
            await db.SeedValuationLineAsync(userId, portfolioId, Guid.NewGuid(), jan, 1000m, cancellationToken, AssetClass.Cash);
            await db.SeedValuationLineAsync(userId, portfolioId, Guid.NewGuid(), mid, 1000m, cancellationToken, AssetClass.Cash);
            await db.SeedValuationLineAsync(userId, portfolioId, Guid.NewGuid(), last, 1000m, cancellationToken, AssetClass.Cash);
            await db.SeedValuationLineAsync(userId, portfolioId, Guid.NewGuid(), last, 700m, cancellationToken, AssetClass.Etf);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("MAX", assetClass: AssetClass.Etf), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([jan, mid, last], body!.Points.Select(p => p.Date).ToArray());
        Assert.Equal([0m, 0m, 700m], body.Points.Select(p => p.NetWorthPln).ToArray());
        Assert.Equal(700m, body.ChangePln);
        Assert.Null(body.ChangePercent);
    }

    [Fact]
    public async Task Get_ClassSeriesSumToTheUnfilteredSeries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var portfolioA = Guid.NewGuid();
        var portfolioB = Guid.NewGuid();
        var classes = new[] { AssetClass.Cash, AssetClass.Etf, AssetClass.Stock };
        var days = new[] { Today.AddDays(-30), Today.AddDays(-15), Today.AddDays(-3), Today };

        await using (var db = CreateDbContext(userId))
        {
            for (var d = 0; d < days.Length; d++)
            {
                decimal totalA = 0m;
                decimal totalB = 0m;
                for (var c = 0; c < classes.Length; c++)
                {
                    var a = 100m * (c + 1) + d * 7.25m;
                    var b = 10m * (c + 1) + d * 1.13m;
                    totalA += a;
                    totalB += b;
                    await db.SeedValuationLineAsync(userId, portfolioA, Guid.NewGuid(), days[d], a, cancellationToken, classes[c]);
                    await db.SeedValuationLineAsync(userId, portfolioB, Guid.NewGuid(), days[d], b, cancellationToken, classes[c]);
                }

                await db.SeedSnapshotAsync(userId, portfolioA, days[d], totalA, cancellationToken);
                await db.SeedSnapshotAsync(userId, portfolioB, days[d], totalB, cancellationToken);
            }
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var all = await GetSeriesAsync(client, null, cancellationToken);
        var sums = new decimal[days.Length];
        foreach (var assetClass in classes)
        {
            var series = await GetSeriesAsync(client, assetClass, cancellationToken);
            Assert.Equal(all.Count, series.Count);
            for (var i = 0; i < series.Count; i++)
            {
                Assert.Equal(all[i].Date, series[i].Date);
                sums[i] += series[i].NetWorthPln;
            }
        }

        Assert.Equal(all.Select(p => p.NetWorthPln).ToArray(), sums);
    }

    [Fact]
    public async Task Get_WithAssetClassAndPortfolioId_ReturnsOnlyThatPortfoliosClass()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var portfolioA = Guid.NewGuid();
        var portfolioB = Guid.NewGuid();

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, portfolioA, Today, 300m, cancellationToken);
            await db.SeedSnapshotAsync(userId, portfolioB, Today, 900m, cancellationToken);
            await db.SeedValuationLineAsync(userId, portfolioA, Guid.NewGuid(), Today, 300m, cancellationToken, AssetClass.Etf);
            await db.SeedValuationLineAsync(userId, portfolioB, Guid.NewGuid(), Today, 900m, cancellationToken, AssetClass.Etf);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("MAX", portfolioA, AssetClass.Etf), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var point = Assert.Single(body!.Points);
        Assert.Equal(300m, point.NetWorthPln);
    }

    [Fact]
    public async Task Get_WithAssetClass_NeverIncludesAnotherUsersValuations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var portfolioA = Guid.NewGuid();
        var portfolioB = Guid.NewGuid();

        await using (var dbA = CreateDbContext(userAId))
        {
            await dbA.SeedSnapshotAsync(userAId, portfolioA, Today, 5000m, cancellationToken);
            await dbA.SeedValuationLineAsync(userAId, portfolioA, Guid.NewGuid(), Today, 5000m, cancellationToken, AssetClass.Etf);
        }

        await using (var dbB = CreateDbContext(userBId))
        {
            await dbB.SeedSnapshotAsync(userBId, portfolioB, Today, 120m, cancellationToken);
            await dbB.SeedValuationLineAsync(userBId, portfolioB, Guid.NewGuid(), Today, 120m, cancellationToken, AssetClass.Etf);
        }

        using var userBClient = Factory.CreateAuthenticatedClient(userBId);
        var response = await userBClient.GetAsync(NetWorthHistoryUri("MAX", assetClass: AssetClass.Etf), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        var point = Assert.Single(body!.Points);
        Assert.Equal(120m, point.NetWorthPln);
    }

    [Fact]
    public async Task Get_WithUndefinedAssetClass_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(NetWorthHistoryUri("1Y", assetClass: (AssetClass)42), cancellationToken);
        var problem = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Validation.InvalidAssetClass", problem);
    }

    private static async Task<IReadOnlyList<NetWorthHistoryPoint>> GetSeriesAsync(
        HttpClient client, AssetClass? assetClass, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(NetWorthHistoryUri("MAX", assetClass: assetClass), cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);
        return body!.Points;
    }
}
