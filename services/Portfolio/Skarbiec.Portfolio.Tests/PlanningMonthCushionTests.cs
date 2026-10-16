using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningMonthCushionTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static AllocationBucketView Bucket(AllocationView allocation, string name) =>
        allocation.Buckets.Single(b => b.Name == name);

    [Fact]
    public async Task FillsGapFirst()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateCushionSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan());

        var allocation = await client.GetAllocationAsync("2026-10", cancellationToken);

        Assert.Equal(["Poduszka", "Wakacje", "Inwestycje"], allocation.Buckets.Select(b => b.Name));
        Assert.Equal([5_200m, 500m, 6_543.27m], allocation.Buckets.Select(b => b.AllocatedPln));
        Assert.Equal(CushionKind, allocation.Buckets[0].Kind);
        var cushion = allocation.Buckets[0].Cushion;
        Assert.NotNull(cushion);
        Assert.Equal(20_000m, cushion.BalanceStartPln);
        Assert.Equal(25_200m, cushion.BalanceEndPln);
        Assert.Equal(25_200m, cushion.TargetPln);
        Assert.Equal(6, cushion.TargetMonths);
        Assert.Null(Bucket(allocation, "Wakacje").Cushion);
    }

    [Fact]
    public async Task ReachedTarget_TakesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateCushionSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan());

        var allocation = await client.GetAllocationAsync("2026-11", cancellationToken);

        Assert.Equal([0m, 500m, 11_743.27m], allocation.Buckets.Select(b => b.AllocatedPln));
        var cushion = allocation.Buckets[0].Cushion;
        Assert.NotNull(cushion);
        Assert.True(cushion.Reached);
        Assert.Equal(25_200m, cushion.BalanceStartPln);
        Assert.Equal(25_200m, cushion.BalanceEndPln);
    }

    [Fact]
    public async Task CostRise_ReopensGap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var expenseId = await client.CreateCushionSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan());
        await client.UpdateRecurringExpenseAsync(
            expenseId, "2027-01", cancellationToken, name: "Życie", amount: 4_500m, dayOfMonth: 1);

        var january = await client.GetAllocationAsync("2027-01", cancellationToken);
        var december = await client.GetAllocationAsync("2026-12", cancellationToken);

        var cushion = january.Buckets[0].Cushion;
        Assert.NotNull(cushion);
        Assert.Equal(27_000m, cushion.TargetPln);
        Assert.Equal(1_800m, january.Buckets[0].AllocatedPln);
        Assert.Equal(0m, december.Buckets[0].AllocatedPln);
    }

    [Fact]
    public async Task Deficit_KeepsBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateCushionSetupAsync(cancellationToken);
        await client.CreateRecurringExpenseAsync(
            cancellationToken, "Wkład własny", 13_000m, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 31), dayOfMonth: 5);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan());

        var october = await client.GetAllocationAsync("2026-10", cancellationToken);
        var november = await client.GetAllocationAsync("2026-11", cancellationToken);

        Assert.Equal(3, october.Buckets.Count);
        Assert.All(october.Buckets, bucket => Assert.Equal(0m, bucket.AllocatedPln));
        var cushion = november.Buckets[0].Cushion;
        Assert.NotNull(cushion);
        Assert.Equal(20_000m, cushion.BalanceStartPln);
        Assert.Equal(5_200m, november.Buckets[0].AllocatedPln);
    }

    [Fact]
    public async Task SubZlotyGap_CountsAsReached()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateCushionSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan(20_000.40m));

        var october = await client.GetAllocationAsync("2026-10", cancellationToken);
        var november = await client.GetAllocationAsync("2026-11", cancellationToken);

        Assert.Equal(5_199m, october.Buckets[0].AllocatedPln);
        Assert.Equal(6_544.27m, Bucket(october, "Inwestycje").AllocatedPln);
        Assert.Equal(0m, november.Buckets[0].AllocatedPln);
        Assert.NotNull(november.Buckets[0].Cushion);
        Assert.True(november.Buckets[0].Cushion!.Reached);
        Assert.Equal(25_199.40m, november.Buckets[0].Cushion!.BalanceStartPln);

        using var other = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await other.CreateCushionSetupAsync(cancellationToken);
        await other.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan(30_000m));

        var over = await other.GetAllocationAsync("2026-10", cancellationToken);

        Assert.Equal(0m, over.Buckets[0].AllocatedPln);
        Assert.NotNull(over.Buckets[0].Cushion);
        Assert.True(over.Buckets[0].Cushion!.Reached);
        Assert.Equal(30_000m, over.Buckets[0].Cushion!.BalanceEndPln);
    }

    [Fact]
    public async Task BeforeFirstVersion_Empty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateCushionSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan());

        var september = await client.GetAllocationAsync("2026-09", cancellationToken);

        Assert.Empty(september.Buckets);
    }

    [Fact]
    public async Task DeletingFirstVersion_KeepsCushionStartingBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateCushionSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan());
        var cushionId = (await client.GetAllocationAsync("2026-10", cancellationToken)).Buckets[0].BucketId;
        await client.SaveAllocationPlanAsync(
            "2026-12",
            cancellationToken,
            CushionRow(bucketId: cushionId),
            AllocationRow("Inwestycje", InvestmentsKind, PercentShare, 100m, isRemainder: true));
        await client.DeleteAllocationPlanVersionAsync("2026-10", cancellationToken);

        var allocation = await client.GetAllocationAsync("2026-12", cancellationToken);

        var cushion = allocation.Buckets[0].Cushion;
        Assert.NotNull(cushion);
        Assert.Equal(20_000m, cushion.BalanceStartPln);
        Assert.Equal(5_200m, allocation.Buckets[0].AllocatedPln);
    }

    [Fact]
    public async Task QueryCount_IndependentOfSpan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateCushionSetupAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan());
        await client.GetPlanningMonthAsync("2026-11", cancellationToken);

        Factory.Commands.Reset();
        await client.GetPlanningMonthAsync("2026-11", cancellationToken);
        var near = Factory.Commands.Count;

        Factory.Commands.Reset();
        var far = await client.GetAllocationAsync("2036-10", cancellationToken);
        var distant = Factory.Commands.Count;

        Assert.True(near > 0);
        Assert.Equal(near, distant);
        Assert.NotNull(far.Buckets[0].Cushion);
    }

    [Fact]
    public async Task Get_ByStranger_SeesNoCushion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await owner.CreateCushionSetupAsync(cancellationToken);
        await owner.SaveAllocationPlanAsync("2026-10", cancellationToken, CushionPlan());
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var allocation = await stranger.GetAllocationAsync("2026-10", cancellationToken);

        Assert.Empty(allocation.Buckets);
    }
}
