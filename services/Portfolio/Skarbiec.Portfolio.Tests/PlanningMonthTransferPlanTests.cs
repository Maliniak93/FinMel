using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.TransferPlan;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningMonthTransferPlanTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Preview_FromBucketsAndReplicas()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateTransferPlanSetupAsync(cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);

        var plan = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        Assert.True(plan.Status.Is(TransferPlanStatus.Preview));
        Assert.Null(plan.NoneReason);
        Assert.Null(plan.FallbackReason);
        Assert.Equal(100, plan.MinimumTransferPln);
        Assert.Equal(TransferValuationDate, plan.ValuationDate);
        Assert.False(plan.ValuationStale);
        Assert.Equal(6_543.27m, plan.ToInvestPln);
        Assert.Equal(
            [
                ("IKE", "IKE mBank", "Akcje/ETF", 2_260m, TransferMarker.AboveTarget),
                ("IKZE", "IKZE Obligacje", "Obligacje", 2_304m, TransferMarker.None),
                ("Inwestycje", null, "Obligacje", 1_300.27m, TransferMarker.None),
                ("Inwestycje", null, "Kruszce", 679m, TransferMarker.None)
            ],
            plan.Rows.Select(r => (r.BucketName, r.AccountName, r.GroupName, r.AmountPln, MarkerOf(r))));
        Assert.Equal(
            [
                ("Akcje/ETF", 60.4m, 59.5m, 50m),
                ("Obligacje", 33.0m, 33.7m, 40m),
                ("Kruszce", 6.6m, 6.7m, 10m)
            ],
            plan.Groups.Select(g => (g.Name, g.CurrentPercent, g.AfterPercent, g.TargetPercent)));
    }

    [Fact]
    public async Task EdgeStates_FallbackReasons()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateTransferPlanSetupAsync(cancellationToken);

        await SeedReplicasAsync(userId, cancellationToken, null, TransferValues);
        var noTarget = await client.GetTransferPlanAsync("2026-10", cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, null);
        var noValuation = await client.GetTransferPlanAsync("2026-10", cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, [new(AssetClass.Cash, 5_000m)]);
        var emptyBase = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        Assert.True(noTarget.FallbackReason.Is(TransferPlanFallbackReason.NoTargetAllocation));
        Assert.True(noValuation.FallbackReason.Is(TransferPlanFallbackReason.NoValuation));
        Assert.True(emptyBase.FallbackReason.Is(TransferPlanFallbackReason.EmptyBase));
        foreach (var fallback in new[] { noTarget, noValuation, emptyBase })
        {
            Assert.True(fallback.Status.Is(TransferPlanStatus.Preview));
            Assert.Empty(fallback.Groups);
            Assert.Equal(
                [("IKE", 2_260m), ("IKZE", 2_304m), ("Inwestycje", 1_979.27m)],
                fallback.Rows.Select(r => (r.BucketName, r.AmountPln)));
            Assert.All(fallback.Rows, r => Assert.Null(r.GroupName));
        }
    }

    [Fact]
    public async Task EdgeStates_BeforeStartFutureMonthAndNothingToInvest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateAllocationSetupAsync(cancellationToken);
        await client.CreateRecurringExpenseAsync(
            cancellationToken, "Wkład własny", 13_000m, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 30), dayOfMonth: 5);
        await client.SaveAllocationPlanAsync("2026-07", cancellationToken, FirstAllocationPlan());
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);

        var beforeStart = await client.GetTransferPlanAsync("2026-08", cancellationToken);
        var future = await client.GetTransferPlanAsync("2026-11", cancellationToken);
        Factory.Clock.SetUtcNow(AfterDueDayUtc);
        var nothingToInvest = await client.GetTransferPlanAsync("2026-11", cancellationToken);
        var startMonth = await client.GetTransferPlanAsync("2026-10", cancellationToken);

        Assert.True(beforeStart.Status.Is(TransferPlanStatus.None));
        Assert.True(beforeStart.NoneReason.Is(TransferPlanNoneReason.BeforeStart));
        Assert.True(future.Status.Is(TransferPlanStatus.None));
        Assert.True(future.NoneReason.Is(TransferPlanNoneReason.FutureMonth));
        Assert.True(nothingToInvest.Status.Is(TransferPlanStatus.None));
        Assert.True(nothingToInvest.NoneReason.Is(TransferPlanNoneReason.NothingToInvest));
        Assert.True(startMonth.Status.Is(TransferPlanStatus.Preview));
    }

    private static TransferMarker MarkerOf(TransferRowView row) =>
        Enum.GetValues<TransferMarker>().Single(marker => row.Marker.Is(marker));
}
