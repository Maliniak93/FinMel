using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Features.TargetAllocation.GetTargetAllocation;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingApi;

namespace Skarbiec.Reporting.Tests;

// The PUT, GET and DELETE under test are called directly; ReportingApi helpers only arrange.
public sealed class GroupTargetAllocationEndpointTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    private static readonly DateOnly SnapshotDate = new(2026, 8, 1);

    [Fact]
    public async Task Put_ReplacesWholeAllocation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var groupId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.SaveGroupTargetAllocationAsync(
            groupId,
            cancellationToken,
            TargetGroup("Stare", [AssetClass.Stock], 60m),
            TargetGroup("Inne", [AssetClass.Bond], 40m));

        var response = await client.PutAsJsonAsync(
            GroupTargetAllocationUri(groupId),
            new
            {
                groups = new[]
                {
                    TargetGroup("  Kruszce ", [AssetClass.PreciousMetal], 25.5m, 2.5m),
                    TargetGroup("Reszta", [AssetClass.Etf, AssetClass.Cash], 74.5m),
                },
            },
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var body = await client.GetGroupTargetAllocationAsync(groupId, cancellationToken);
        Assert.True(body.IsConfigured);
        Assert.Equal(["Kruszce", "Reszta"], body.Groups.Select(g => g.Name));
        Assert.Equal([25.5m, 74.5m], body.Groups.Select(g => g.TargetPercent));
        Assert.Equal(2.5m, body.Groups[0].BandPp);
        Assert.Equal([AssetClass.PreciousMetal], body.Groups[0].AssetClasses);
    }

    [Fact]
    public async Task InvalidAllocations_Return400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var groupId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.SaveGroupTargetAllocationAsync(groupId, cancellationToken, TargetGroup("Akcje", [AssetClass.Stock], 100m));

        var cases = new (object[] Groups, string Code)[]
        {
            ([TargetGroup("A", [AssetClass.Stock], 60m), TargetGroup("B", [AssetClass.Etf], 39.9m)], "Validation.TargetAllocationSum"),
            ([TargetGroup("A", [AssetClass.Stock], 50m), TargetGroup("B", [AssetClass.Stock, AssetClass.Etf], 50m)], "Validation.AssetClassInManyGroups"),
            ([TargetGroup("A", [AssetClass.Stock], 50m), TargetGroup("B", [], 50m)], "Validation.GroupWithoutAssetClass"),
            ([TargetGroup("Akcje", [AssetClass.Stock], 50m), TargetGroup(" akcje", [AssetClass.Etf], 50m)], "Validation.DuplicateGroupName"),
            ([TargetGroup("A", [AssetClass.Stock], 100.1m)], "Validation.InvalidGroup"),
            ([TargetGroup("A", [AssetClass.Stock], 100m, -1m)], "Validation.InvalidGroup"),
        };

        foreach (var (groups, code) in cases)
        {
            var response = await client.PutAsJsonAsync(GroupTargetAllocationUri(groupId), new { groups }, cancellationToken);

            await response.AssertProblemAsync(HttpStatusCode.BadRequest, code, cancellationToken);
        }

        var saved = await client.GetGroupTargetAllocationAsync(groupId, cancellationToken);
        var group = Assert.Single(saved.Groups);
        Assert.Equal("Akcje", group.Name);
        Assert.Equal(100m, group.TargetPercent);
    }

    [Fact]
    public async Task Allocations_AreIndependent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstGroup = Guid.NewGuid();
        var secondGroup = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.SaveTargetAllocationAsync(cancellationToken, TargetGroup("Globalna", [AssetClass.Stock], 100m));
        await client.SaveGroupTargetAllocationAsync(firstGroup, cancellationToken, TargetGroup("Pierwsza", [AssetClass.Bond], 100m));
        await client.SaveGroupTargetAllocationAsync(secondGroup, cancellationToken, TargetGroup("Druga", [AssetClass.Etf], 100m));

        await client.SaveGroupTargetAllocationAsync(firstGroup, cancellationToken, TargetGroup("Zmieniona", [AssetClass.Cash], 100m));

        Assert.Equal(["Globalna"], (await client.GetTargetAllocationAsync(cancellationToken)).Groups.Select(g => g.Name));
        Assert.Equal(["Druga"], (await client.GetGroupTargetAllocationAsync(secondGroup, cancellationToken)).Groups.Select(g => g.Name));

        using var deleted = await client.DeleteAsync(GroupTargetAllocationUri(firstGroup), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(["Globalna"], (await client.GetTargetAllocationAsync(cancellationToken)).Groups.Select(g => g.Name));
        Assert.Equal(["Druga"], (await client.GetGroupTargetAllocationAsync(secondGroup, cancellationToken)).Groups.Select(g => g.Name));
    }

    [Fact]
    public async Task Get_ComputesFromGroupAssetsOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        await ArrangeGroupAsync(userId, groupId, cancellationToken, includeCash: false);

        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.SaveGroupTargetAllocationAsync(groupId, cancellationToken, BondStockEtfMetal());

        var response = await client.GetAsync(GroupTargetAllocationUri(groupId), cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<TargetAllocationResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body!.IsConfigured);
        Assert.Equal(SnapshotDate, body.AsOf);
        Assert.Equal(55_000m, body.TotalPln);
        Assert.Equal(55_000m, body.BasePln);
        Assert.Equal(0m, body.OutsidePln);
        Assert.Equal(
            [
                (30_000m, 54.5m, 55m, 65m, -3_000m),
                (20_000m, 36.4m, 25m, 35m, 3_500m),
                (5_000m, 9.1m, 7m, 13m, -500m),
            ],
            body.Groups.Select(g => (g.ValuePln, g.CurrentPercent!.Value, g.BandLowPercent, g.BandHighPercent, g.DeviationPln!.Value)));
        Assert.Equal([-5.5m, 6.4m, -0.9m], body.Groups.Select(g => g.DeviationPp!.Value));
    }

    [Fact]
    public async Task Get_UnallocatedClassIsOutside()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        await ArrangeGroupAsync(userId, groupId, cancellationToken, includeCash: true);

        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.SaveGroupTargetAllocationAsync(groupId, cancellationToken, BondStockEtfMetal());

        var body = await client.GetGroupTargetAllocationAsync(groupId, cancellationToken);

        Assert.Equal(60_000m, body.TotalPln);
        Assert.Equal(55_000m, body.BasePln);
        Assert.Equal(5_000m, body.OutsidePln);
        Assert.Equal([AssetClass.Cash], body.OutsideAssetClasses);
        Assert.Equal([-3_000m, 3_500m, -500m], body.Groups.Select(g => g.DeviationPln!.Value));
    }

    [Fact]
    public async Task Get_FollowsCurrentAssignment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();
        Guid movedAssetId;
        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, SnapshotDate, 15_000m, cancellationToken);
            await db.SeedGroupAssetAsync(userId, groupA, SnapshotDate, AssetClass.Bond, 1_000m, cancellationToken);
            movedAssetId = await db.SeedGroupAssetAsync(userId, groupA, SnapshotDate, AssetClass.Bond, 2_000m, cancellationToken);
            await db.SeedGroupAssetAsync(userId, groupB, SnapshotDate, AssetClass.Bond, 4_000m, cancellationToken);
            await db.SeedGroupAssetAsync(userId, null, SnapshotDate, AssetClass.Bond, 8_000m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.SaveGroupTargetAllocationAsync(groupA, cancellationToken, TargetGroup("Obligacje", [AssetClass.Bond], 100m));
        await client.SaveGroupTargetAllocationAsync(groupB, cancellationToken, TargetGroup("Obligacje", [AssetClass.Bond], 100m));

        Assert.Equal(3_000m, (await client.GetGroupTargetAllocationAsync(groupA, cancellationToken)).TotalPln);
        Assert.Equal(4_000m, (await client.GetGroupTargetAllocationAsync(groupB, cancellationToken)).TotalPln);

        await using (var db = CreateDbContext(userId))
        {
            await db.Positions.Where(p => p.AssetId == movedAssetId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.GroupId, groupB), cancellationToken);
        }

        Assert.Equal(1_000m, (await client.GetGroupTargetAllocationAsync(groupA, cancellationToken)).TotalPln);
        Assert.Equal(6_000m, (await client.GetGroupTargetAllocationAsync(groupB, cancellationToken)).TotalPln);
    }

    [Fact]
    public async Task Get_EdgeStates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var savedGroup = Guid.NewGuid();
        await using (var db = CreateDbContext(userId))
        {
            await db.SeedSnapshotAsync(userId, SnapshotDate, 1_000m, cancellationToken);
            await db.SeedGroupAssetAsync(userId, Guid.NewGuid(), SnapshotDate, AssetClass.Stock, 1_000m, cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        var nothingSaved = await client.GetGroupTargetAllocationAsync(Guid.NewGuid(), cancellationToken);

        Assert.False(nothingSaved.IsConfigured);
        Assert.Empty(nothingSaved.Groups);
        Assert.Equal(0m, nothingSaved.TotalPln);

        await client.SaveGroupTargetAllocationAsync(savedGroup, cancellationToken, TargetGroup("Akcje", [AssetClass.Stock], 100m));
        var unknown = await client.GetGroupTargetAllocationAsync(Guid.NewGuid(), cancellationToken);

        Assert.False(unknown.IsConfigured);
        Assert.Empty(unknown.Groups);

        var noValue = await client.GetGroupTargetAllocationAsync(savedGroup, cancellationToken);

        Assert.True(noValue.IsConfigured);
        Assert.Equal(0m, noValue.TotalPln);
        var group = Assert.Single(noValue.Groups);
        Assert.Equal("Akcje", group.Name);
        Assert.Equal(0m, group.ValuePln);
        Assert.Null(group.CurrentPercent);
        Assert.Null(group.DeviationPp);
        Assert.Null(group.TargetValuePln);
        Assert.Null(group.DeviationPln);
        Assert.Null(group.IsInBand);
    }

    [Fact]
    public async Task Delete_RemovesAndIsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var groupId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.SaveGroupTargetAllocationAsync(groupId, cancellationToken, TargetGroup("Akcje", [AssetClass.Stock], 100m));

        var first = await client.DeleteAsync(GroupTargetAllocationUri(groupId), cancellationToken);
        var second = await client.DeleteAsync(GroupTargetAllocationUri(groupId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var body = await client.GetGroupTargetAllocationAsync(groupId, cancellationToken);
        Assert.False(body.IsConfigured);
        Assert.Empty(body.Groups);
    }

    private static object[] BondStockEtfMetal() => new object[]
    {
        TargetGroup("Obligacje", [AssetClass.Bond], 60m),
        TargetGroup("Akcje/ETF", [AssetClass.Stock, AssetClass.Etf], 30m),
        TargetGroup("Kruszce", [AssetClass.PreciousMetal], 10m, 3m),
    };

    private async Task ArrangeGroupAsync(Guid userId, Guid groupId, CancellationToken cancellationToken, bool includeCash)
    {
        await using var db = CreateDbContext(userId);
        await db.SeedSnapshotAsync(userId, SnapshotDate, includeCash ? 60_000m : 55_000m, cancellationToken);
        await db.SeedGroupAssetAsync(userId, groupId, SnapshotDate, AssetClass.Bond, 30_000m, cancellationToken);
        await db.SeedGroupAssetAsync(userId, groupId, SnapshotDate, AssetClass.Etf, 20_000m, cancellationToken);
        await db.SeedGroupAssetAsync(userId, groupId, SnapshotDate, AssetClass.PreciousMetal, 5_000m, cancellationToken);
        if (includeCash)
        {
            await db.SeedGroupAssetAsync(userId, groupId, SnapshotDate, AssetClass.Cash, 5_000m, cancellationToken);
        }
    }
}
