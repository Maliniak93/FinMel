using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioAssertions;

namespace Skarbiec.Portfolio.Tests;

// The planning month is the thing under test; the fixtures only arrange. Today is 2026-10-20, so 2026-10 is the current month.
public sealed class PlanningMonthSplitTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task AddsContributionsFromTheCurrentMonth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateFreeFundsOf7000Async(cancellationToken);
        await client.CreateSplitGroupsAsync(cancellationToken);

        foreach (var month in new[] { "2026-10", "2026-12" })
        {
            var view = await client.GetPlanningMonthAsync(month, cancellationToken);

            var contributions = Present(view.Expenses.GroupContributions);
            Assert.Equal(
                new Dictionary<string, decimal> { ["Emerytura"] = 2_000m, ["Inwestycje"] = 1_000m },
                contributions.ToDictionary(c => c.Name, c => c.AmountPln));
            Assert.Equal(4_000m, view.Expenses.CostPln);
            Assert.Equal(4_000m, view.FreeFunds.PlanPln);
        }
    }

    [Fact]
    public async Task PastMonthHasNoContributionsAndNoSplit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateFreeFundsOf7000Async(cancellationToken);
        await client.CreateSplitGroupsAsync(cancellationToken);

        var past = await client.GetPlanningMonthAsync("2026-09", cancellationToken);
        var current = await client.GetPlanningMonthAsync("2026-10", cancellationToken);

        Assert.Empty(Present(past.Expenses.GroupContributions));
        Assert.Null(past.Split);
        Assert.Equal(1_000m, past.Expenses.CostPln);
        Assert.Equal(7_000m, past.FreeFunds.PlanPln);
        Assert.NotNull(current.Split);
    }

    [Fact]
    public async Task SplitsFreeFundsByCushionGapThenSharesAndBreaksContributionsDown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateFreeFundsOf7000Async(cancellationToken);
        var groups = await client.CreateSplitGroupsAsync(cancellationToken);
        await SeedValuationReplicaAsync(
            userId,
            cancellationToken,
            (groups.CushionId, [(AssetClass.BankAccount, 10_500m)]),
            (groups.RetirementId, [(AssetClass.Deposit, 9_000m), (AssetClass.BankAccount, 1_000m)]));

        var split = Present((await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Split);

        Assert.False(split.ValuationMissing);
        Assert.Equal(4_000m, split.FreeFundsPln);
        Assert.Null(split.DeficitPln);
        Assert.Equal(0m, split.UnassignedPln);
        var cushion = Present(split.Cushion);
        Assert.Equal(
            (groups.CushionId, 10_500m, 12_000m, 1_500m, 1_500m),
            (cushion.GroupId, cushion.LevelPln, cushion.TargetPln, cushion.MissingPln, cushion.AllocationPln));

        var retirement = Assert.Single(split.Groups, g => g.GroupId == groups.RetirementId);
        var investing = Assert.Single(split.Groups, g => g.GroupId == groups.InvestingId);
        Assert.Equal((50m, 2_000m, 1_250m), (retirement.SharePercent, retirement.ContributionPln, retirement.AllocationPln));
        Assert.Equal((50m, 1_000m, 1_250m), (investing.SharePercent, investing.ContributionPln, investing.AllocationPln));
        Assert.Equal(
            new Dictionary<AssetClass, decimal> { [AssetClass.Deposit] = 0m, [AssetClass.BankAccount] = 2_000m },
            Present(retirement.Breakdown).ToDictionary(b => b.AssetClass, b => b.AmountPln));
        Assert.Equal(
            new Dictionary<AssetClass, decimal> { [AssetClass.Deposit] = 500m, [AssetClass.BankAccount] = 500m },
            Present(investing.Breakdown).ToDictionary(b => b.AssetClass, b => b.AmountPln));
    }

    [Fact]
    public async Task CushionFillsCumulativelyInLaterMonths()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateFreeFundsOf7000Async(cancellationToken);
        var groups = await client.CreateSplitGroupsAsync(cancellationToken);
        await SeedValuationReplicaAsync(
            userId, cancellationToken, (groups.CushionId, [(AssetClass.BankAccount, 10_500m)]));

        var current = Present((await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Split);
        var next = Present((await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Split);
        var later = Present((await client.GetPlanningMonthAsync("2027-02", cancellationToken)).Split);

        Assert.Equal((10_500m, 1_500m, 1_500m), (current.Cushion!.LevelPln, current.Cushion.MissingPln, current.Cushion.AllocationPln));
        Assert.All(current.Groups, g => Assert.Equal(1_250m, g.AllocationPln));
        Assert.Equal((12_000m, 0m, 0m), (next.Cushion!.LevelPln, next.Cushion.MissingPln, next.Cushion.AllocationPln));
        Assert.All(next.Groups, g => Assert.Equal(2_000m, g.AllocationPln));
        Assert.Equal((12_000m, 0m, 0m), (later.Cushion!.LevelPln, later.Cushion.MissingPln, later.Cushion.AllocationPln));
    }

    [Fact]
    public async Task CushionLevelGrowsByItsOwnContributionWhichCountsAsAnExpense()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateFreeFundsOf7000Async(cancellationToken);
        var groups = await client.CreateSplitGroupsAsync(cancellationToken, cushionContributionPln: 500m);
        await SeedValuationReplicaAsync(
            userId, cancellationToken, (groups.CushionId, [(AssetClass.BankAccount, 8_000m)]));

        var currentView = await client.GetPlanningMonthAsync("2026-10", cancellationToken);
        var nextView = await client.GetPlanningMonthAsync("2026-11", cancellationToken);

        Assert.Equal(500m, Assert.Single(Present(currentView.Expenses.GroupContributions), c => c.Name == "Poduszka").AmountPln);
        Assert.Equal(4_500m, currentView.Expenses.CostPln);
        Assert.Equal(3_500m, currentView.FreeFunds.PlanPln);
        var current = Present(currentView.Split).Cushion!;
        Assert.Equal((8_000m, 4_000m, 3_500m), (current.LevelPln, current.MissingPln, current.AllocationPln));
        var next = Present(nextView.Split);
        Assert.Equal(
            (12_000m, 0m, 0m), (next.Cushion!.LevelPln, next.Cushion.MissingPln, next.Cushion.AllocationPln));
        Assert.All(next.Groups, g => Assert.Equal(1_750m, g.AllocationPln));
    }

    [Fact]
    public async Task WithoutAValuationTheCushionStartsEmptyAndContributionsFollowPercentages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateFreeFundsOf7000Async(cancellationToken);
        var groups = await client.CreateSplitGroupsAsync(cancellationToken);

        var split = Present((await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Split);

        Assert.True(split.ValuationMissing);
        var cushion = Present(split.Cushion);
        Assert.Equal((0m, 12_000m, 4_000m), (cushion.LevelPln, cushion.MissingPln, cushion.AllocationPln));
        var retirement = Assert.Single(split.Groups, g => g.GroupId == groups.RetirementId);
        Assert.Equal(0m, retirement.AllocationPln);
        Assert.Equal(
            new Dictionary<AssetClass, decimal> { [AssetClass.Deposit] = 1_000m, [AssetClass.BankAccount] = 1_000m },
            Present(retirement.Breakdown).ToDictionary(b => b.AssetClass, b => b.AmountPln));
    }
}
