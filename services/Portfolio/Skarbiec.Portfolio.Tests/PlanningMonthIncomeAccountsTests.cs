using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.GroupFundingApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningMonthIncomeAccountsTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private const string November = "2026-11";

    [Fact]
    public async Task PlanSubtractsOnlyIncomeAccountContributions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateBankAccountAsync(cancellationToken, "Pensja");
        var b = await client.CreateBankAccountAsync(cancellationToken, "Oszczędnościowe");
        await client.CreateRecurringIncomeAsync(
            a.AccountId, cancellationToken, "Wypłata", 10_000m, new DateOnly(2026, 11, 1), dayOfMonth: 10);
        var groupId = await client.CreateFundingGroupAsync(
            cancellationToken, "Emerytura", 4_000m,
            contributions: [new ContributionBody(2_000m, a.AccountId), new ContributionBody(2_000m, b.AccountId)]);

        var month = await client.GetPlanningMonthAsync(November, cancellationToken);

        Assert.Equal(8_000m, month.FreeFunds.PlanPln);
        var row = Assert.Single(month.Expenses.GroupContributions!, c => c.GroupId == groupId);
        Assert.Equal((4_000m, 2_000m), (row.AmountPln, row.FromIncomePln));
        Assert.Equal(2, row.Contributions!.Count);
        Assert.True(Assert.Single(row.Contributions, c => c.AccountId == a.AccountId).FromIncome);
        Assert.False(Assert.Single(row.Contributions, c => c.AccountId == b.AccountId).FromIncome);
    }

    [Fact]
    public async Task ContributionWithoutAccountCounts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateBankAccountAsync(cancellationToken, "Pensja");
        await client.CreateRecurringIncomeAsync(
            a.AccountId, cancellationToken, "Wypłata", 10_000m, new DateOnly(2026, 11, 1), dayOfMonth: 10);
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Emerytura", 1_500m);

        var month = await client.GetPlanningMonthAsync(November, cancellationToken);

        Assert.Equal(8_500m, month.FreeFunds.PlanPln);
        var row = Assert.Single(month.Expenses.GroupContributions!, c => c.GroupId == groupId);
        Assert.Equal((1_500m, 1_500m), (row.AmountPln, row.FromIncomePln));
    }

    [Fact]
    public async Task ActiveRecurringIncomeWithoutOccurrenceCounts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateBankAccountAsync(cancellationToken, "Pensja");
        await client.CreateRecurringIncomeAsync(
            a.AccountId, cancellationToken, "Premia kwartalna", 3_000m, new DateOnly(2026, 10, 1),
            intervalMonths: 3, dayOfMonth: 10);
        var groupId = await client.CreateFundingGroupAsync(
            cancellationToken, "Emerytura", 2_000m, contributions: [new ContributionBody(2_000m, a.AccountId)]);

        var month = await client.GetPlanningMonthAsync(November, cancellationToken);

        var row = Assert.Single(month.Expenses.GroupContributions!, c => c.GroupId == groupId);
        Assert.Equal(2_000m, row.FromIncomePln);
        Assert.Equal(-2_000m, month.FreeFunds.PlanPln);
    }

    [Fact]
    public async Task EndedRecurringIncomeDoesNotCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateBankAccountAsync(cancellationToken, "Pensja");
        await client.CreateRecurringIncomeAsync(
            a.AccountId, cancellationToken, "Stara praca", 3_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 30),
            dayOfMonth: 10);
        var groupId = await client.CreateFundingGroupAsync(
            cancellationToken, "Emerytura", 2_000m, contributions: [new ContributionBody(2_000m, a.AccountId)]);

        var month = await client.GetPlanningMonthAsync(November, cancellationToken);

        var row = Assert.Single(month.Expenses.GroupContributions!, c => c.GroupId == groupId);
        Assert.Equal((2_000m, 0m), (row.AmountPln, row.FromIncomePln));
        Assert.Equal(0m, month.FreeFunds.PlanPln);
    }

    [Fact]
    public async Task OneOffIncomeAccountCountsInItsMonth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var c = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe");
        await client.CreateOneOffIncomeAsync(c.AccountId, cancellationToken, "Premia", 5_000m, new DateOnly(2026, 11, 15));
        var groupId = await client.CreateFundingGroupAsync(
            cancellationToken, "Emerytura", 1_000m, contributions: [new ContributionBody(1_000m, c.AccountId)]);

        var november = await client.GetPlanningMonthAsync(November, cancellationToken);
        var december = await client.GetPlanningMonthAsync("2026-12", cancellationToken);

        Assert.Equal(1_000m, Assert.Single(november.Expenses.GroupContributions!, g => g.GroupId == groupId).FromIncomePln);
        Assert.Equal(0m, Assert.Single(december.Expenses.GroupContributions!, g => g.GroupId == groupId).FromIncomePln);
    }

    [Fact]
    public async Task UnplannedIncomeIsNoPlanIncomeAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var c = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "C", balance: 5_000m);
        var incomeId = await client.CreateOneOffIncomeAsync(
            c.AccountId, cancellationToken, "Premia", 5_000m, new DateOnly(2026, 10, 12), planned: false);
        var groupId = await client.CreateFundingGroupAsync(
            cancellationToken, "Poduszka", 1_000m, contributions: [new ContributionBody(1_000m, c.AccountId)]);
        var target = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Poduszka PLN");

        var plan = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);
        var planRow = Assert.Single(plan.Expenses.GroupContributions!, g => g.GroupId == groupId);
        Assert.Equal(0m, planRow.FromIncomePln);
        Assert.False(Assert.Single(planRow.Contributions!, x => x.AccountId == c.AccountId).FromIncome);

        await client.SettleOneOffIncomeAsync(incomeId, c.AccountId, 5_000m, new DateOnly(2026, 10, 12), cancellationToken);
        await client.TransferMoneyAsync(c.MoneyAssetId, target.MoneyAssetId, 1_000m, FundingDay, cancellationToken);

        var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);
        var deposit = Assert.Single(month.Expenses.GroupDeposits!);
        Assert.Equal((groupId, 1_000m), (deposit.GroupId, deposit.FromIncomePln));
    }
}
