using Skarbiec.Contracts;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingApi;

namespace Skarbiec.Reporting.Tests;

public sealed class TargetAllocationTenancyIsolationTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    [Fact]
    public async Task SavedTarget_IsInvisibleAndUnaffectedAcrossUsers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var date = new DateOnly(2026, 8, 1);
        await using (var db = CreateDbContext(userAId))
        {
            await db.SeedValuationAsync(userAId, date, [(AssetClass.Stock, 6_000m), (AssetClass.Bond, 4_000m)], cancellationToken);
        }

        await using (var db = CreateDbContext(userBId))
        {
            await db.SeedValuationAsync(userBId, date, [(AssetClass.Stock, 1_000m), (AssetClass.Bond, 9_000m)], cancellationToken);
        }

        using var clientA = Factory.CreateAuthenticatedClient(userAId);
        using var clientB = Factory.CreateAuthenticatedClient(userBId);

        await clientA.SaveTargetAllocationAsync(
            cancellationToken,
            TargetGroup("Akcje", [AssetClass.Stock], 70m),
            TargetGroup("Obligacje", [AssetClass.Bond], 30m));

        var bBefore = await clientB.GetTargetAllocationAsync(cancellationToken);
        Assert.False(bBefore.IsConfigured);
        Assert.Empty(bBefore.Groups);

        var aBefore = await clientA.GetTargetAllocationAsync(cancellationToken);
        await clientB.SaveTargetAllocationAsync(cancellationToken, TargetGroup("Wszystko", [AssetClass.Stock, AssetClass.Bond], 100m));
        var aAfter = await clientA.GetTargetAllocationAsync(cancellationToken);

        Assert.Equal(["Akcje", "Obligacje"], aAfter.Groups.Select(g => g.Name));
        Assert.Equal(
            aBefore.Groups.Select(g => (g.TargetPercent, g.ValuePln, g.CurrentPercent, g.DeviationPp, g.DeviationPln)),
            aAfter.Groups.Select(g => (g.TargetPercent, g.ValuePln, g.CurrentPercent, g.DeviationPp, g.DeviationPln)));
        var bAfter = await clientB.GetTargetAllocationAsync(cancellationToken);
        Assert.Equal(["Wszystko"], bAfter.Groups.Select(g => g.Name));
    }
}
