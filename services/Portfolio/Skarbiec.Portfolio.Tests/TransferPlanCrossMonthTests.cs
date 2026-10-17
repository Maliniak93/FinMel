using Skarbiec.Portfolio.Features.TransferPlan;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class TransferPlanCrossMonthTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task OtherMonthsRows_CountInThePreview_UntilChecked()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await SetUpTransferPlanFromAsync(client, userId, "2026-08", cancellationToken);
        var baseline = await client.GetTransferPlanAsync("2026-10", cancellationToken);
        await client.SkipTransferPlanAsync("2026-08", cancellationToken);
        await client.AcceptTransferPlanAsync("2026-09", cancellationToken);
        var september = await client.GetTransferPlanAsync("2026-09", cancellationToken);
        await client.CheckTransferPlanRowAsync(september.RowOf("Inwestycje", "Kruszce").Id!.Value, true, cancellationToken);

        var october = await client.GetTransferPlanAsync("2026-10", cancellationToken);
        await client.CheckTransferPlanRowAsync(september.RowOf("IKZE", "Obligacje").Id!.Value, true, cancellationToken);
        var afterCheck = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        Assert.True(october.Status.Is(TransferPlanStatus.Preview));
        Assert.Equal(
            [("Akcje/ETF", 60.4m), ("Obligacje", 33.0m), ("Kruszce", 6.6m)],
            baseline.Groups.Select(g => (g.Name, g.CurrentPercent)));
        Assert.Equal(
            [("Akcje/ETF", 59.8m), ("Obligacje", 33.9m), ("Kruszce", 6.4m)],
            october.Groups.Select(g => (g.Name, g.CurrentPercent)));
        Assert.Equal(
            [("Akcje/ETF", 60.5m), ("Obligacje", 33.0m), ("Kruszce", 6.5m)],
            afterCheck.Groups.Select(g => (g.Name, g.CurrentPercent)));
    }
}
