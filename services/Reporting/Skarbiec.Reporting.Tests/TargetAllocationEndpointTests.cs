using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Features.TargetAllocation.GetTargetAllocation;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingApi;

namespace Skarbiec.Reporting.Tests;

// The PUT and GET under test are called directly; ReportingApi helpers only arrange.
public sealed class TargetAllocationEndpointTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    private static readonly DateOnly SnapshotDate = new(2026, 8, 1);

    [Fact]
    public async Task Get_ComputesDeviationsFromLatestValuation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        await using (var db = CreateDbContext(userId))
        {
            await db.SeedValuationAsync(userId, SnapshotDate.AddDays(-1), [(AssetClass.Stock, 1m)], cancellationToken);
            await db.SeedValuationAsync(
                userId,
                SnapshotDate,
                [
                    (AssetClass.Stock, 80_000m),
                    (AssetClass.Etf, 30_000m),
                    (AssetClass.Bond, 50_000m),
                    (AssetClass.Deposit, 10_000m),
                    (AssetClass.PreciousMetal, 10_000m),
                    (AssetClass.Cash, 5_000m),
                    (AssetClass.BankAccount, 15_000m),
                ],
                cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.SaveTargetAllocationAsync(
            cancellationToken,
            TargetGroup("Akcje/ETF", [AssetClass.Stock, AssetClass.Etf], 50m),
            TargetGroup("Obligacje", [AssetClass.Bond, AssetClass.Deposit], 40m),
            TargetGroup("Kruszce", [AssetClass.PreciousMetal], 10m));

        var response = await client.GetAsync(TargetAllocationUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<TargetAllocationResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body!.IsConfigured);
        Assert.Equal(SnapshotDate, body.AsOf);
        Assert.False(body.IsStale);
        Assert.Equal(200_000m, body.TotalPln);
        Assert.Equal(180_000m, body.BasePln);
        Assert.Equal(20_000m, body.OutsidePln);
        Assert.Equal([AssetClass.Cash, AssetClass.BankAccount], body.OutsideAssetClasses.OrderBy(c => c));
        Assert.Equal(7, body.ByAssetClass.Count);
        Assert.Equal(80_000m, Assert.Single(body.ByAssetClass, c => c.AssetClass == AssetClass.Stock).ValuePln);

        Assert.Equal(["Akcje/ETF", "Obligacje", "Kruszce"], body.Groups.Select(g => g.Name));
        Assert.Equal(
            [
                (110_000m, 61.1m, 11.1m, 45m, 55m, false, 20_000m),
                (60_000m, 33.3m, -6.7m, 35m, 45m, false, -12_000m),
                (10_000m, 5.6m, -4.4m, 5m, 15m, true, -8_000m),
            ],
            body.Groups.Select(g => (g.ValuePln, g.CurrentPercent!.Value, g.DeviationPp!.Value, g.BandLowPercent, g.BandHighPercent, g.IsInBand!.Value, g.DeviationPln!.Value)));
        Assert.Equal([90_000m, 72_000m, 18_000m], body.Groups.Select(g => g.TargetValuePln!.Value));
    }

    [Fact]
    public async Task Get_EdgeStates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var noSnapshotUser = Guid.NewGuid();
        var onlyBankUser = Guid.NewGuid();
        var staleUser = Guid.NewGuid();
        var unconfiguredUser = Guid.NewGuid();

        await using (var db = CreateDbContext(onlyBankUser))
        {
            await db.SeedValuationAsync(onlyBankUser, SnapshotDate, [(AssetClass.BankAccount, 5_000m)], cancellationToken);
        }

        await using (var db = CreateDbContext(staleUser))
        {
            await db.SeedValuationAsync(staleUser, SnapshotDate, [(AssetClass.Stock, 1_000m)], cancellationToken, isStale: true);
        }

        await using (var db = CreateDbContext(unconfiguredUser))
        {
            await db.SeedValuationAsync(
                unconfiguredUser, SnapshotDate, [(AssetClass.Stock, 1_000m), (AssetClass.Cash, 500m)], cancellationToken);
        }

        var stockEtf = TargetGroup("Akcje/ETF", [AssetClass.Stock, AssetClass.Etf], 100m);

        using var noSnapshotClient = Factory.CreateAuthenticatedClient(noSnapshotUser);
        await noSnapshotClient.SaveTargetAllocationAsync(cancellationToken, stockEtf);
        var noSnapshot = await noSnapshotClient.GetTargetAllocationAsync(cancellationToken);

        Assert.True(noSnapshot.IsConfigured);
        Assert.Null(noSnapshot.AsOf);
        var noSnapshotGroup = Assert.Single(noSnapshot.Groups);
        Assert.Equal(0m, noSnapshotGroup.ValuePln);
        Assert.Null(noSnapshotGroup.CurrentPercent);
        Assert.Null(noSnapshotGroup.DeviationPp);
        Assert.Null(noSnapshotGroup.TargetValuePln);
        Assert.Null(noSnapshotGroup.DeviationPln);
        Assert.Null(noSnapshotGroup.IsInBand);

        using var onlyBankClient = Factory.CreateAuthenticatedClient(onlyBankUser);
        await onlyBankClient.SaveTargetAllocationAsync(cancellationToken, stockEtf);
        var onlyBank = await onlyBankClient.GetTargetAllocationAsync(cancellationToken);

        Assert.Equal(0m, onlyBank.BasePln);
        Assert.Equal(5_000m, onlyBank.OutsidePln);
        var onlyBankGroup = Assert.Single(onlyBank.Groups);
        Assert.Equal(0m, onlyBankGroup.ValuePln);
        Assert.Null(onlyBankGroup.CurrentPercent);
        Assert.Null(onlyBankGroup.DeviationPp);
        Assert.Null(onlyBankGroup.TargetValuePln);
        Assert.Null(onlyBankGroup.DeviationPln);
        Assert.Null(onlyBankGroup.IsInBand);

        using var staleClient = Factory.CreateAuthenticatedClient(staleUser);
        await staleClient.SaveTargetAllocationAsync(cancellationToken, stockEtf);
        Assert.True((await staleClient.GetTargetAllocationAsync(cancellationToken)).IsStale);

        using var unconfiguredClient = Factory.CreateAuthenticatedClient(unconfiguredUser);
        var unconfigured = await unconfiguredClient.GetTargetAllocationAsync(cancellationToken);

        Assert.False(unconfigured.IsConfigured);
        Assert.Empty(unconfigured.Groups);
        Assert.Equal(0m, unconfigured.BasePln);
        Assert.Equal(1_500m, unconfigured.TotalPln);
        Assert.Equal(1_500m, unconfigured.OutsidePln);
        Assert.Equal(2, unconfigured.ByAssetClass.Count);
    }

    [Fact]
    public async Task Put_ReplacesWholeAllocation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.SaveTargetAllocationAsync(
            cancellationToken,
            TargetGroup("Stare", [AssetClass.Stock], 60m),
            TargetGroup("Inne", [AssetClass.Bond], 40m));

        var response = await client.PutAsJsonAsync(
            TargetAllocationUri,
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
        var body = await client.GetTargetAllocationAsync(cancellationToken);
        Assert.Equal(["Kruszce", "Reszta"], body.Groups.Select(g => g.Name));
        Assert.Equal([25.5m, 74.5m], body.Groups.Select(g => g.TargetPercent));
        Assert.Equal(2.5m, body.Groups[0].BandPp);
        Assert.Equal([AssetClass.PreciousMetal], body.Groups[0].AssetClasses);
    }

    [Fact]
    public async Task InvalidAllocations_Return400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.SaveTargetAllocationAsync(cancellationToken, TargetGroup("Akcje", [AssetClass.Stock], 100m));

        var cases = new (object[] Groups, string Code)[]
        {
            ([TargetGroup("A", [AssetClass.Stock], 60m), TargetGroup("B", [AssetClass.Etf], 39.9m)], "Validation.TargetAllocationSum"),
            ([], "Validation.TargetAllocationSum"),
            ([TargetGroup("A", [AssetClass.Stock], 50m), TargetGroup("B", [AssetClass.Stock, AssetClass.Etf], 50m)], "Validation.AssetClassInManyGroups"),
            ([TargetGroup("A", [AssetClass.Stock], 50m), TargetGroup("B", [], 50m)], "Validation.GroupWithoutAssetClass"),
            ([TargetGroup("Akcje", [AssetClass.Stock], 50m), TargetGroup(" akcje", [AssetClass.Etf], 50m)], "Validation.DuplicateGroupName"),
            ([TargetGroup("A", [AssetClass.Stock], 100.1m)], "Validation.InvalidGroup"),
            ([TargetGroup("A", [AssetClass.Stock], 100m, -1m)], "Validation.InvalidGroup"),
            ([TargetGroup("A", [AssetClass.Stock], 12.25m), TargetGroup("B", [AssetClass.Etf], 87.75m)], "Validation.InvalidGroup"),
            ([TargetGroup("", [AssetClass.Stock], 100m)], "Validation.InvalidGroup"),
            ([new { name = "A", assetClasses = new[] { 7 }, targetPercent = 100m, bandPp = 5m }], "Validation.InvalidGroup"),
        };

        foreach (var (groups, code) in cases)
        {
            var response = await client.PutAsJsonAsync(TargetAllocationUri, new { groups }, cancellationToken);

            await response.AssertProblemAsync(HttpStatusCode.BadRequest, code, cancellationToken);
        }

        var saved = await client.GetTargetAllocationAsync(cancellationToken);
        var group = Assert.Single(saved.Groups);
        Assert.Equal("Akcje", group.Name);
        Assert.Equal(100m, group.TargetPercent);
    }
}
