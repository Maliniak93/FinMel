using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class TransferPlanGroupValuesTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task FrozenRow_CountsTowardsTheGroupOfItsLowestClass()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await SetUpTransferPlanFromAsync(client, userId, "2026-09", cancellationToken);
        await client.AcceptTransferPlanAsync("2026-09", cancellationToken);
        await SeedReplicasAsync(
            userId,
            cancellationToken,
            [
                new("Akcje", [AssetClass.Stock], 50m, 5m),
                new("ETF", [AssetClass.Etf], 50m, 5m)
            ],
            [new(AssetClass.Stock, 70_000m), new(AssetClass.Etf, 30_000m)],
            TransferValuationDate);

        var october = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        Assert.Equal(
            [("Akcje", 70.7m), ("ETF", 29.3m)],
            october.Groups.Select(g => (g.Name, g.CurrentPercent)));
    }

    [Fact]
    public async Task FrozenRowWithoutAGroup_CountsNowhere()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await SetUpTransferPlanFromAsync(client, userId, "2026-09", cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, null, TransferValues, TransferValuationDate);
        await client.AcceptTransferPlanAsync("2026-09", cancellationToken);
        await SeedReplicasAsync(
            userId,
            cancellationToken,
            [
                new("Akcje", [AssetClass.Stock], 60m, 5m),
                new("Obligacje", [AssetClass.Bond], 40m, 5m)
            ],
            [new(AssetClass.Stock, 60_000m), new(AssetClass.Bond, 40_000m)],
            TransferValuationDate);

        var october = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        Assert.Equal(
            [("Akcje", 60.0m), ("Obligacje", 40.0m)],
            october.Groups.Select(g => (g.Name, g.CurrentPercent)));
    }
}
