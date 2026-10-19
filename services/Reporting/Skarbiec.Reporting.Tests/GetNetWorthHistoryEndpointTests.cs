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
        var tooOld = Today.AddMonths(-2);
        var withinRange = Today.AddDays(-5);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, tooOld, 100m, cancellationToken);
            await db.SeedSnapshotAsync(userId, withinRange, 200m, cancellationToken);
            await db.SeedSnapshotAsync(userId, Today, 300m, cancellationToken);
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
        var lastYearEnd = new DateOnly(Today.Year - 1, 12, 31);
        var yearStart = new DateOnly(Today.Year, 1, 1);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, lastYearEnd, 100m, cancellationToken);
            await db.SeedSnapshotAsync(userId, yearStart, 200m, cancellationToken);
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
        var earliest = Today.AddYears(-5);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, earliest, 50m, cancellationToken);
            await db.SeedSnapshotAsync(userId, Today, 300m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("MAX"), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Equal(earliest, body!.Points.First().Date);
        Assert.Equal(50m, body.Points.First().NetWorthPln);
    }

    [Fact]
    public async Task Get_ThreeSnapshotDates_OnePointPerDateWithSnapshotTotal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var days = new[] { Today.AddDays(-20), Today.AddDays(-10), Today };
        var totals = new[] { 1000m, 1500m, 1250m };

        await using (var db = CreateDbContext(userId))
        {
            for (var i = 0; i < days.Length; i++)
            {
                await db.SeedSnapshotAsync(userId, days[i], totals[i], cancellationToken);
            }
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("MAX"), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(days, body!.Points.Select(p => p.Date).ToArray());
        Assert.Equal(totals, body.Points.Select(p => p.NetWorthPln).ToArray());
    }

    [Fact]
    public async Task Get_LeavesGapsUnfilled_ForDatesWithNoSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var day1 = Today.AddDays(-2);
        var day3 = Today;

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, day1, 100m, cancellationToken);
            await db.SeedSnapshotAsync(userId, day3, 110m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("MAX"), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Equal(2, body!.Points.Count);
        Assert.DoesNotContain(body.Points, p => p.Date == Today.AddDays(-1));
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

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, Today.AddDays(-30), 1000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, Today.AddDays(-10), 5000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, Today, 1250m, cancellationToken);
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
            await db.SeedSnapshotAsync(userId, Today, 1000m, cancellationToken);
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

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, Today.AddDays(-5), 0m, cancellationToken);
            await db.SeedSnapshotAsync(userId, Today, 500m, cancellationToken);
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
            await db.SeedSnapshotAsync(userAId, Today, 5000m, cancellationToken);
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
                await db.SeedSnapshotAsync(userId, days[i], etfA[i] + etfB[i] + 1500m, cancellationToken);
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
        var accountId = Guid.NewGuid();
        var jan = new DateOnly(Today.Year - 1, Today.Month, 1).AddDays(1);
        var mid = Today.AddMonths(-6);
        var last = Today;

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, jan, 1000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, mid, 1000m, cancellationToken);
            await db.SeedSnapshotAsync(userId, last, 1700m, cancellationToken);
            await db.SeedValuationLineAsync(userId, accountId, Guid.NewGuid(), jan, 1000m, cancellationToken, AssetClass.Cash);
            await db.SeedValuationLineAsync(userId, accountId, Guid.NewGuid(), mid, 1000m, cancellationToken, AssetClass.Cash);
            await db.SeedValuationLineAsync(userId, accountId, Guid.NewGuid(), last, 1000m, cancellationToken, AssetClass.Cash);
            await db.SeedValuationLineAsync(userId, accountId, Guid.NewGuid(), last, 700m, cancellationToken, AssetClass.Etf);
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

                await db.SeedSnapshotAsync(userId, days[d], totalA + totalB, cancellationToken);
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
    public async Task Get_WithAssetClass_NeverIncludesAnotherUsersValuations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var portfolioA = Guid.NewGuid();
        var portfolioB = Guid.NewGuid();

        await using (var dbA = CreateDbContext(userAId))
        {
            await dbA.SeedSnapshotAsync(userAId, Today, 5000m, cancellationToken);
            await dbA.SeedValuationLineAsync(userAId, portfolioA, Guid.NewGuid(), Today, 5000m, cancellationToken, AssetClass.Etf);
        }

        await using (var dbB = CreateDbContext(userBId))
        {
            await dbB.SeedSnapshotAsync(userBId, Today, 120m, cancellationToken);
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

    [Fact]
    public async Task ClassFilter_FollowsTimeline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.WaitUntilReadyAsync(cancellationToken);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedPositionAsync(Guid.NewGuid(), userId, accountId, cancellationToken,
                assetClass: AssetClass.Etf, valuationMode: AssetValuationMode.CurrencyValued, quantity: 400m,
                quantityHistory: [(Today.AddDays(-10), 400m)],
                classHistory: [(Today.AddDays(-10), AssetClass.Cash), (Today.AddDays(-6), AssetClass.Stock), (Today.AddDays(-3), AssetClass.Etf)]);
            await db.RebuildHistoryAsync(Bus, userId, Today.AddDays(-10), cancellationToken);
        }

        for (var offset = 10; offset >= 1; offset--)
        {
            Assert.Equal(400m, Assert.Single(await GetSeriesAsync(client, null, cancellationToken), p => p.Date == Today.AddDays(-offset)).NetWorthPln);
        }

        foreach (var (assetClass, firstOffset, lastOffset) in new[] { (AssetClass.Cash, 10, 7), (AssetClass.Stock, 6, 4), (AssetClass.Etf, 3, 1) })
        {
            var series = await GetSeriesAsync(client, assetClass, cancellationToken);
            for (var offset = 10; offset >= 1; offset--)
            {
                var expected = offset <= firstOffset && offset >= lastOffset ? 400m : 0m;
                Assert.Equal(expected, Assert.Single(series, p => p.Date == Today.AddDays(-offset)).NetWorthPln);
            }
        }
    }

    [Fact]
    public async Task GroupFilter_SumsGroupLines()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var group = Guid.NewGuid();
        var inGroup1 = Guid.NewGuid();
        var inGroup2 = Guid.NewGuid();
        var other = Guid.NewGuid();
        var days = new[] { Today.AddDays(-20), Today.AddDays(-10), Today };
        var first = new[] { 100m, 150m, 200m };
        var second = new[] { 50m, 50m, 100m };

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedPositionAsync(inGroup1, userId, null, cancellationToken, groupId: group);
            await db.SeedPositionAsync(inGroup2, userId, null, cancellationToken, groupId: group);
            await db.SeedPositionAsync(other, userId, null, cancellationToken);
            for (var i = 0; i < days.Length; i++)
            {
                await db.SeedSnapshotAsync(userId, days[i], first[i] + second[i] + 1000m, cancellationToken);
                await db.SeedValuationLineAsync(userId, null, inGroup1, days[i], first[i], cancellationToken);
                await db.SeedValuationLineAsync(userId, null, inGroup2, days[i], second[i], cancellationToken);
                await db.SeedValuationLineAsync(userId, null, other, days[i], 1000m, cancellationToken);
            }
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync(NetWorthHistoryUri("1Y", groupId: group), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(days, body!.Points.Select(p => p.Date).ToArray());
        Assert.Equal([150m, 200m, 300m], body.Points.Select(p => p.NetWorthPln).ToArray());
    }

    [Fact]
    public async Task GroupFilter_FollowsCurrentAssignment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();
        var moved = Guid.NewGuid();
        var stays = Guid.NewGuid();
        var days = new[] { Today.AddDays(-20), Today.AddDays(-10), Today };

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedPositionAsync(moved, userId, null, cancellationToken, groupId: groupB);
            await db.SeedPositionAsync(stays, userId, null, cancellationToken, groupId: groupA);
            foreach (var day in days)
            {
                await db.SeedSnapshotAsync(userId, day, 700m, cancellationToken);
                await db.SeedValuationLineAsync(userId, null, moved, day, 300m, cancellationToken);
                await db.SeedValuationLineAsync(userId, null, stays, day, 400m, cancellationToken);
            }
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var seriesA = await GetFilteredSeriesAsync(client, cancellationToken, groupId: groupA);
        var seriesB = await GetFilteredSeriesAsync(client, cancellationToken, groupId: groupB);

        Assert.Equal([400m, 400m, 400m], seriesA.Select(p => p.NetWorthPln).ToArray());
        Assert.Equal([300m, 300m, 300m], seriesB.Select(p => p.NetWorthPln).ToArray());
        Assert.Equal(days, seriesB.Select(p => p.Date).ToArray());
    }

    [Fact]
    public async Task GroupAndClassFilter()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var group = Guid.NewGuid();
        var etfInGroup = Guid.NewGuid();
        var cashInGroup = Guid.NewGuid();
        var etfOutside = Guid.NewGuid();
        var days = new[] { Today.AddDays(-10), Today };

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedPositionAsync(etfInGroup, userId, null, cancellationToken, assetClass: AssetClass.Etf, groupId: group);
            await db.SeedPositionAsync(cashInGroup, userId, null, cancellationToken, assetClass: AssetClass.Cash, groupId: group);
            await db.SeedPositionAsync(etfOutside, userId, null, cancellationToken, assetClass: AssetClass.Etf);
            foreach (var day in days)
            {
                await db.SeedSnapshotAsync(userId, day, 1_110m, cancellationToken);
                await db.SeedValuationLineAsync(userId, null, etfInGroup, day, 100m, cancellationToken, AssetClass.Etf);
                await db.SeedValuationLineAsync(userId, null, cashInGroup, day, 10m, cancellationToken, AssetClass.Cash);
                await db.SeedValuationLineAsync(userId, null, etfOutside, day, 1_000m, cancellationToken, AssetClass.Etf);
            }
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var series = await GetFilteredSeriesAsync(client, cancellationToken, groupId: group, assetClass: AssetClass.Etf);

        Assert.Equal([100m, 100m], series.Select(p => p.NetWorthPln).ToArray());
    }

    [Fact]
    public async Task Ungrouped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var group = Guid.NewGuid();
        var grouped = Guid.NewGuid();
        var loose = Guid.NewGuid();
        var days = new[] { Today.AddDays(-10), Today };

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedPositionAsync(grouped, userId, null, cancellationToken, groupId: group);
            await db.SeedPositionAsync(loose, userId, null, cancellationToken);
            foreach (var day in days)
            {
                await db.SeedSnapshotAsync(userId, day, 350m, cancellationToken);
                await db.SeedValuationLineAsync(userId, null, grouped, day, 300m, cancellationToken);
                await db.SeedValuationLineAsync(userId, null, loose, day, 50m, cancellationToken);
            }
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var series = await GetFilteredSeriesAsync(client, cancellationToken, ungrouped: true);

        Assert.Equal([50m, 50m], series.Select(p => p.NetWorthPln).ToArray());
    }

    [Fact]
    public async Task GroupAndUngrouped_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        using var response = await client.GetAsync(
            NetWorthHistoryUri("MAX", groupId: Guid.NewGuid(), ungrouped: true), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "Validation.InvalidGroupFilter", cancellationToken);
    }

    [Fact]
    public async Task ArchivedAsset_StaysInGroup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var group = Guid.NewGuid();
        var archived = Guid.NewGuid();
        var active = Guid.NewGuid();
        var days = new[] { Today.AddDays(-20), Today.AddDays(-10), Today.AddDays(-5), Today };
        var archiveDate = Today.AddDays(-8);

        await using (var db = CreateDbContext(userId))
        {
            await db.SeedPositionAsync(archived, userId, null, cancellationToken,
                groupId: group, isArchived: true, archivedOn: archiveDate);
            await db.SeedPositionAsync(active, userId, null, cancellationToken);
            foreach (var day in days)
            {
                await db.SeedSnapshotAsync(userId, day, 600m, cancellationToken);
                await db.SeedValuationLineAsync(userId, null, active, day, 500m, cancellationToken);
                if (day < archiveDate)
                {
                    await db.SeedValuationLineAsync(userId, null, archived, day, 100m, cancellationToken);
                }
            }
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var series = await GetFilteredSeriesAsync(client, cancellationToken, groupId: group);

        Assert.Equal(days, series.Select(p => p.Date).ToArray());
        Assert.Equal([100m, 100m, 0m, 0m], series.Select(p => p.NetWorthPln).ToArray());
    }

    private static async Task<IReadOnlyList<NetWorthHistoryPoint>> GetFilteredSeriesAsync(
        HttpClient client,
        CancellationToken cancellationToken,
        Guid? groupId = null,
        bool? ungrouped = null,
        AssetClass? assetClass = null)
    {
        var response = await client.GetAsync(NetWorthHistoryUri("MAX", assetClass, groupId, ungrouped), cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<NetWorthHistoryResponse>(cancellationToken);
        return body!.Points;
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
