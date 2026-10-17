using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningMonthTaxLimitTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static AllocationBucketView Bucket(AllocationView allocation, string name) =>
        allocation.Buckets.Single(b => b.Name == name);

    [Fact]
    public async Task PromptExample_October()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ike, ikze) = await client.CreateTaxLimitSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, TaxLimitPlan(ike, ikze));

        var allocation = await client.GetAllocationAsync("2026-10", cancellationToken);

        Assert.Equal(["Poduszka", "Wakacje", "IKE", "IKZE", "Inwestycje"], allocation.Buckets.Select(b => b.Name));
        Assert.Equal([5_200m, 500m, 2_260m, 2_304m, 1_979.27m], allocation.Buckets.Select(b => b.AllocatedPln));
        Assert.Equal(12_243.27m, allocation.Buckets.Sum(b => b.AllocatedPln));
        Assert.Equal(6_543.27m, allocation.ToInvestPln);
        Assert.Equal(IkeKind, Bucket(allocation, "IKE").Kind);
        var ikeLimit = Bucket(allocation, "IKE").Limit;
        Assert.NotNull(ikeLimit);
        Assert.Equal(new LimitView(2026, 28_260m, 26_000m, 28_260m, true, false), ikeLimit);
        Assert.Equal(new LimitView(2026, 11_304m, 9_000m, 11_304m, true, false), Bucket(allocation, "IKZE").Limit);
        Assert.Null(Bucket(allocation, "Wakacje").Limit);
        Assert.Null(Bucket(allocation, "Inwestycje").Limit);
    }

    [Fact]
    public async Task FullLimits_OverflowToRemainder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ike, ikze) = await client.CreateTaxLimitSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, TaxLimitPlan(ike, ikze));

        var allocation = await client.GetAllocationAsync("2026-11", cancellationToken);

        Assert.Equal([0m, 500m, 0m, 0m, 11_743.27m], allocation.Buckets.Select(b => b.AllocatedPln));
        Assert.True(Bucket(allocation, "IKE").Limit!.Reached);
        Assert.True(Bucket(allocation, "IKZE").Limit!.Reached);
        Assert.Equal(28_260m, Bucket(allocation, "IKE").Limit!.ContributedStartPln);
    }

    [Fact]
    public async Task NewYear_ResetsCounters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ike, ikze) = await client.CreateTaxLimitSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, TaxLimitPlan(ike, ikze));

        var allocation = await client.GetAllocationAsync("2027-01", cancellationToken);

        Assert.Equal([0m, 500m, 5_871m, 2_348m, 3_524.27m], allocation.Buckets.Select(b => b.AllocatedPln));
        Assert.Equal(new LimitView(2027, 28_260m, 0m, 5_871m, false, true), Bucket(allocation, "IKE").Limit);
        Assert.Equal(new LimitView(2027, 11_304m, 0m, 2_348m, false, true), Bucket(allocation, "IKZE").Limit);
    }

    [Fact]
    public async Task FixedCappedByLimit_ExcessToRemainder()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAllocationSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync(
            "2026-10",
            cancellationToken,
            AllocationRow("IKE", IkeKind, FixedShare, 3_000m, startingBalance: 26_000m),
            AllocationRow("Inwestycje", InvestmentsKind, PercentShare, 100m, isRemainder: true));

        var capped = await client.GetAllocationAsync("2026-10", cancellationToken);

        Assert.Equal([2_260m, 9_983.27m], capped.Buckets.Select(b => b.AllocatedPln));
        Assert.True(capped.Buckets[0].LimitReached);
        Assert.False(capped.Buckets[0].Underfunded);

        await client.SaveAllocationPlanAsync(
            "2026-10",
            cancellationToken,
            AllocationRow("Remont", SavingsKind, FixedShare, 11_000m),
            AllocationRow("IKE", IkeKind, FixedShare, 3_000m, startingBalance: 0m),
            AllocationRow("Inwestycje", InvestmentsKind, PercentShare, 100m, isRemainder: true));

        var underfunded = await client.GetAllocationAsync("2026-10", cancellationToken);

        Assert.Equal([11_000m, 1_243m, 0.27m], underfunded.Buckets.Select(b => b.AllocatedPln));
        Assert.True(underfunded.Buckets[1].Underfunded);
        Assert.False(underfunded.Buckets[1].LimitReached);
    }

    [Fact]
    public async Task NoBucketBelow_OverflowToRemainderAbove()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAllocationSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync(
            "2026-10",
            cancellationToken,
            AllocationRow("Inwestycje", InvestmentsKind, PercentShare, 40m, isRemainder: true),
            AllocationRow("IKE", IkeKind, PercentShare, 60m, startingBalance: 26_000m));

        var allocation = await client.GetAllocationAsync("2026-10", cancellationToken);

        Assert.Equal(["Inwestycje", "IKE"], allocation.Buckets.Select(b => b.Name));
        Assert.Equal([9_983.27m, 2_260m], allocation.Buckets.Select(b => b.AllocatedPln));
    }

    [Fact]
    public async Task TwoIkeBuckets_SeparateCounters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAllocationSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync(
            "2026-10",
            cancellationToken,
            AllocationRow("IKE Dominik", IkeKind, PercentShare, 50m, startingBalance: 26_000m),
            AllocationRow("IKE Żona", IkeKind, PercentShare, 30m, startingBalance: 0m),
            AllocationRow("Inwestycje", InvestmentsKind, PercentShare, 20m, isRemainder: true));

        var october = await client.GetAllocationAsync("2026-10", cancellationToken);
        var november = await client.GetAllocationAsync("2026-11", cancellationToken);

        Assert.Equal([2_260m, 7_534m, 2_449.27m], october.Buckets.Select(b => b.AllocatedPln));
        Assert.Equal(28_260m, Bucket(october, "IKE Dominik").Limit!.ContributedEndPln);
        Assert.Equal(7_534m, Bucket(october, "IKE Żona").Limit!.ContributedEndPln);
        Assert.Equal([0m, 9_794m, 2_449.27m], november.Buckets.Select(b => b.AllocatedPln));
    }
}
