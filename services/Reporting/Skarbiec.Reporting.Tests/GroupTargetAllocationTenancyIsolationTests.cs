using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingApi;

namespace Skarbiec.Reporting.Tests;

public sealed class GroupTargetAllocationTenancyIsolationTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    [Fact]
    public async Task GroupAllocation_IsInvisibleAndUnaffectedAcrossUsers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var groupId = Guid.NewGuid();
        var date = new DateOnly(2026, 8, 1);
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        await using (var db = CreateDbContext(userAId))
        {
            await db.SeedSnapshotAsync(userAId, date, 6_000m, cancellationToken);
            await db.SeedGroupAssetAsync(userAId, groupId, date, AssetClass.Stock, 6_000m, cancellationToken);
        }

        await using (var db = CreateDbContext(userBId))
        {
            await db.SeedSnapshotAsync(userBId, date, 9_000m, cancellationToken);
            await db.SeedGroupAssetAsync(userBId, groupId, date, AssetClass.Stock, 9_000m, cancellationToken);
        }

        using var clientA = Factory.CreateAuthenticatedClient(userAId);
        using var clientB = Factory.CreateAuthenticatedClient(userBId);
        await clientA.SaveGroupTargetAllocationAsync(groupId, cancellationToken, TargetGroup("Akcje", [AssetClass.Stock], 100m));

        var bBefore = await clientB.GetGroupTargetAllocationAsync(groupId, cancellationToken);
        Assert.False(bBefore.IsConfigured);
        Assert.Empty(bBefore.Groups);

        await clientB.SaveGroupTargetAllocationAsync(groupId, cancellationToken, TargetGroup("Wszystko", [AssetClass.Stock, AssetClass.Bond], 100m));
        var aAfterSave = await clientA.GetGroupTargetAllocationAsync(groupId, cancellationToken);
        Assert.Equal(["Akcje"], aAfterSave.Groups.Select(g => g.Name));
        Assert.Equal(6_000m, aAfterSave.TotalPln);

        using var deleted = await clientB.DeleteAsync(GroupTargetAllocationUri(groupId), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Equal(["Akcje"], (await clientA.GetGroupTargetAllocationAsync(groupId, cancellationToken)).Groups.Select(g => g.Name));
        Assert.False((await clientB.GetGroupTargetAllocationAsync(groupId, cancellationToken)).IsConfigured);
    }
}
