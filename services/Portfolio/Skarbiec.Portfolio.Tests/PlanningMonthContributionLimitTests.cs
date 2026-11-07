using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningMonthContributionLimitTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static readonly DateOnly NovemberStart = new(2026, 11, 1);

    private static readonly DateOnly DecemberEnd = new(2026, 12, 31);

    [Fact]
    public async Task WarnsWhenPlannedIncomeExceedsLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ike = await client.CreateAccountWithCashAsync(cancellationToken, 26_000m, "IKE Broker", wrapper: TaxWrapper.Ike);
        await client.SetContributionLimitsAsync(2026, 28_260m, 11_304m, cancellationToken);
        await client.CreateRecurringIncomeAsync(
            ike.AccountId, cancellationToken, "Wpłata IKE", 1_500m, NovemberStart, DecemberEnd, dayOfMonth: 15);

        var warnings = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).ContributionLimitWarnings;

        var warning = Assert.Single(warnings!);
        Assert.Equal(
            (ike.AccountId, "IKE Broker", 2026, 28_260m, 26_000m, 3_000m, 740m),
            (warning.AccountId, warning.AccountName, warning.Year, warning.LimitPln, warning.ContributedPln, warning.PlannedPln, warning.ExceededByPln));
    }

    [Fact]
    public async Task NoWarningWithinLimitOrWithoutLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var withinLimit = await client.CreateAccountWithCashAsync(cancellationToken, 26_000m, "IKE w limicie", wrapper: TaxWrapper.Ike);
        var noLimitKind = await client.CreateAccountWithCashAsync(cancellationToken, 26_000m, "IKZE bez limitu", wrapper: TaxWrapper.Ikze);
        var regular = await client.CreateAccountWithCashAsync(cancellationToken, 26_000m, "Zwykłe");
        var archived = await client.CreateAccountWithCashAsync(cancellationToken, 30_000m, "IKE zarchiwizowane", wrapper: TaxWrapper.Ike);
        await client.ArchiveAccountAsync(archived.AccountId, cancellationToken);
        await client.SetContributionLimitsAsync(2026, 28_260m, null, cancellationToken);
        await client.CreateRecurringIncomeAsync(withinLimit.AccountId, cancellationToken, "W limicie", 1_500m, NovemberStart, NovemberStart.AddMonths(1).AddDays(-1), dayOfMonth: 15);
        await client.CreateRecurringIncomeAsync(noLimitKind.AccountId, cancellationToken, "Bez limitu", 50_000m, NovemberStart, DecemberEnd, dayOfMonth: 15);
        await client.CreateRecurringIncomeAsync(regular.AccountId, cancellationToken, "Zwykłe", 50_000m, NovemberStart, DecemberEnd, dayOfMonth: 15);

        var warnings = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).ContributionLimitWarnings;

        Assert.Empty(warnings!);
    }

    [Fact]
    public async Task CountsSettledDepositOnceAndIgnoresSkipped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ike = await client.CreateAccountWithCashAsync(cancellationToken, 25_000m, "IKE Broker", wrapper: TaxWrapper.Ike);
        await client.SetContributionLimitsAsync(2026, 28_260m, null, cancellationToken);
        var incomeId = await client.CreateRecurringIncomeAsync(
            ike.AccountId, cancellationToken, "Wpłata IKE", 1_500m, new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 31), dayOfMonth: 15);
        await client.SettleOccurrenceAsync(incomeId, "2026-08", ike.AccountId, 1_500m, new DateOnly(2026, 8, 15), cancellationToken, createDeposit: true);
        await client.SettleOccurrenceAsync(incomeId, "2026-09", ike.AccountId, 1_500m, new DateOnly(2026, 9, 15), cancellationToken);
        await client.SkipOccurrenceAsync(incomeId, "2026-10", cancellationToken);
        await client.CreateOneOffIncomeAsync(ike.AccountId, cancellationToken, "Premia", 3_000m, new DateOnly(2026, 12, 1));

        var warnings = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).ContributionLimitWarnings;

        var warning = Assert.Single(warnings!);
        Assert.Equal((26_500m, 3_000m, 1_240m), (warning.ContributedPln, warning.PlannedPln, warning.ExceededByPln));
    }

    [Fact]
    public async Task CountsPendingOneOffIncome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ike = await client.CreateAccountWithCashAsync(cancellationToken, 26_000m, "IKE Broker", wrapper: TaxWrapper.Ike);
        await client.SetContributionLimitsAsync(2026, 28_260m, null, cancellationToken);
        await client.CreateOneOffIncomeAsync(ike.AccountId, cancellationToken, "Premia", 3_000m, new DateOnly(2026, 12, 20));

        var warnings = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).ContributionLimitWarnings;

        var warning = Assert.Single(warnings!);
        Assert.Equal((3_000m, 740m), (warning.PlannedPln, warning.ExceededByPln));
    }

    [Fact]
    public async Task TenancyIsolation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ike = await owner.CreateAccountWithCashAsync(cancellationToken, 26_000m, "IKE Broker", wrapper: TaxWrapper.Ike);
        await owner.SetContributionLimitsAsync(2026, 28_260m, null, cancellationToken);
        await owner.CreateOneOffIncomeAsync(ike.AccountId, cancellationToken, "Premia", 3_000m, new DateOnly(2026, 12, 20));

        var ownerWarnings = (await owner.GetPlanningMonthAsync("2026-11", cancellationToken)).ContributionLimitWarnings;
        var strangerWarnings = (await stranger.GetPlanningMonthAsync("2026-11", cancellationToken)).ContributionLimitWarnings;

        Assert.Single(ownerWarnings!);
        Assert.Empty(strangerWarnings!);
    }
}
