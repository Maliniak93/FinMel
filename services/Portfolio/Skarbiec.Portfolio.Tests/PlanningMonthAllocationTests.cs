using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningMonthAllocationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task FixedThenPercent_FlooredRemainderTakesRest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAllocationSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, FirstAllocationPlan());

        var allocation = await client.GetAllocationAsync("2026-10", cancellationToken);

        Assert.Equal(["Wakacje", "Akcje", "Lokata", "ETF"], allocation.Buckets.Select(b => b.Name));
        Assert.Equal([500m, 5_871m, 2_348m, 3_524.27m], allocation.Buckets.Select(b => b.AllocatedPln));
        Assert.Equal(12_243.27m, allocation.Buckets.Sum(b => b.AllocatedPln));
        Assert.Equal(12_243.27m, allocation.ForecastPln);
        Assert.Equal(9_395.27m, allocation.ToInvestPln);
        Assert.Equal(0m, allocation.DeficitPln);
        Assert.Equal("2026-10", allocation.EffectiveFrom);
        Assert.Equal([SavingsKind, InvestmentsKind, SavingsKind, InvestmentsKind], allocation.Buckets.Select(b => b.Kind));
        Assert.True(allocation.Buckets.Single(b => b.Name == "ETF").IsRemainder);
    }

    [Fact]
    public async Task UnderfundedFixed_AndPercentShortfallToRemainder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAllocationSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync(
            "2026-10",
            cancellationToken,
            AllocationRow("Wakacje", SavingsKind, FixedShare, 500m),
            AllocationRow("Remont", SavingsKind, FixedShare, 15_000m),
            AllocationRow("ETF", InvestmentsKind, PercentShare, 100m, isRemainder: true));

        var underfunded = await client.GetAllocationAsync("2026-10", cancellationToken);

        Assert.Equal([500m, 11_743m, 0.27m], underfunded.Buckets.Select(b => b.AllocatedPln));
        Assert.Equal([false, true, false], underfunded.Buckets.Select(b => b.Underfunded));

        await client.SaveAllocationPlanAsync(
            "2026-10",
            cancellationToken,
            AllocationRow("Akcje", InvestmentsKind, PercentShare, 50m),
            AllocationRow("ETF", InvestmentsKind, PercentShare, 30m, isRemainder: true));

        var shortfall = await client.GetAllocationAsync("2026-10", cancellationToken);

        Assert.Equal([6_121m, 6_122.27m], shortfall.Buckets.Select(b => b.AllocatedPln));
    }

    [Fact]
    public async Task Deficit_AllZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAllocationSetupAsync(cancellationToken);
        await client.CreateRecurringExpenseAsync(
            cancellationToken, "Wkład własny", 13_000m, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 30), dayOfMonth: 5);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, FirstAllocationPlan());

        var allocation = await client.GetAllocationAsync("2026-11", cancellationToken);

        Assert.Equal(4, allocation.Buckets.Count);
        Assert.All(allocation.Buckets, bucket => Assert.Equal(0m, bucket.AllocatedPln));
        Assert.Equal(756.73m, allocation.DeficitPln);
        Assert.Equal(-756.73m, allocation.ForecastPln);
        Assert.Equal(0m, allocation.ToInvestPln);
    }
}
