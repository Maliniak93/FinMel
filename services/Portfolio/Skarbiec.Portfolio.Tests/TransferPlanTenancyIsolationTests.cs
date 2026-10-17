using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class TransferPlanTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task GetMonth_AsUserB_SeesOnlyOwnGroupsValuesAndMinimum()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        using var clientA = Factory.CreateAuthenticatedClient(userA);
        using var clientB = Factory.CreateAuthenticatedClient(userB);
        await clientA.CreateTransferPlanSetupAsync(cancellationToken);
        await clientB.CreateTransferPlanSetupAsync(cancellationToken);
        await SeedReplicasAsync(userA, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        await SeedReplicasAsync(
            userB,
            cancellationToken,
            [new TargetAllocationReplicaGroup("Wszystko", [AssetClass.Stock, AssetClass.Etf, AssetClass.Bond, AssetClass.Deposit, AssetClass.PreciousMetal], 100m, 5m)],
            [new ValuationReplicaClassValue(AssetClass.Stock, 50_000m)],
            new DateOnly(2026, 10, 10));
        await clientA.SaveTransferPlanSettingsAsync(250, cancellationToken);

        var planA = await clientA.GetTransferPlanAsync("2026-10", cancellationToken);
        var planB = await clientB.GetTransferPlanAsync("2026-10", cancellationToken);

        Assert.Equal(["Akcje/ETF", "Obligacje", "Kruszce"], planA.Groups.Select(g => g.Name));
        Assert.Equal(250, planA.MinimumTransferPln);
        Assert.Equal(TransferValuationDate, planA.ValuationDate);
        Assert.Equal(["Wszystko"], planB.Groups.Select(g => g.Name));
        Assert.Equal(100, planB.MinimumTransferPln);
        Assert.Equal(new DateOnly(2026, 10, 10), planB.ValuationDate);
        Assert.Equal(100m, planB.Groups.Single().CurrentPercent);
    }
}
