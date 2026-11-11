using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.GroupFundingApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningMonthFundingTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task RealisesBySourceAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateIncomeAccountAsync(cancellationToken, "A");
        var b = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "B", balance: 10_000m);
        var groupId = await client.CreateFundingGroupAsync(
            cancellationToken, "Emerytura", 4_000m,
            contributions: [new ContributionBody(2_000m, a.AccountId), new ContributionBody(2_000m, b.AccountId)]);
        var target = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Emerytalne");
        var baseline = (await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).FreeFunds.ActualPln;

        await client.TransferMoneyAsync(a.MoneyAssetId, target.MoneyAssetId, 2_000m, FundingDay, cancellationToken);
        await client.TransferMoneyAsync(b.MoneyAssetId, target.MoneyAssetId, 2_000m, FundingDay, cancellationToken);

        var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);
        var contribution = Assert.Single(month.Expenses.GroupContributions!, c => c.GroupId == groupId);
        Assert.Equal((4_000m, "Settled"), (contribution.FundedPln, contribution.Status));
        var deposit = Assert.Single(month.Expenses.GroupDeposits!);
        Assert.Equal((groupId, 4_000m, 2_000m), (deposit.GroupId, deposit.TotalPln, deposit.FromIncomePln));
        Assert.Equal(baseline - 2_000m, month.FreeFunds.ActualPln);
    }

    [Fact]
    public async Task TransferBeyondPlanIsExcess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateIncomeAccountAsync(cancellationToken, "A");
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Poduszka", 0m);
        var target = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Poduszka PLN");

        await client.TransferMoneyAsync(a.MoneyAssetId, target.MoneyAssetId, 2_000m, FundingDay, cancellationToken);

        var deposit = Assert.Single((await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).Expenses.GroupDeposits!);
        Assert.Equal((groupId, 2_000m, 2_000m), (deposit.GroupId, deposit.ExcessPln, deposit.FromIncomePln));
    }

    [Fact]
    public async Task LeftoverRealisesContributionWithoutAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateIncomeAccountAsync(cancellationToken, "A");
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Poduszka", 1_000m);
        var target = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Poduszka PLN");

        await client.TransferMoneyAsync(a.MoneyAssetId, target.MoneyAssetId, 2_000m, FundingDay, cancellationToken);

        var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);
        Assert.Equal(1_000m, Assert.Single(month.Expenses.GroupContributions!, c => c.GroupId == groupId).FundedPln);
        Assert.Equal(1_000m, Assert.Single(month.Expenses.GroupDeposits!).ExcessPln);
    }

    [Fact]
    public async Task NonIncomeTransferCountsOnlyInTotal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateIncomeAccountAsync(cancellationToken, "A");
        var e1 = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "E1", balance: 1_000m);
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Poduszka", 0m);
        var target = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Poduszka PLN");
        var baseline = (await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).FreeFunds.ActualPln;

        await client.TransferMoneyAsync(e1.MoneyAssetId, target.MoneyAssetId, 500m, FundingDay, cancellationToken);

        var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);
        var deposit = Assert.Single(month.Expenses.GroupDeposits!);
        Assert.Equal((500m, 0m, 0m), (deposit.TotalPln, deposit.ExcessPln, deposit.FromIncomePln));
        Assert.Equal(baseline, month.FreeFunds.ActualPln);
    }

    [Fact]
    public async Task InternalMoveIsNotCounted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateIncomeAccountAsync(cancellationToken, "A");
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Dom", 1_000m);
        var first = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Rezerwa 1", balance: 1_000m);
        var second = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Rezerwa 2", balance: 1_000m);
        var loose = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "Luźne 1", balance: 1_000m);
        var otherLoose = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "Luźne 2");

        await client.TransferMoneyAsync(first.MoneyAssetId, second.MoneyAssetId, 100m, FundingDay, cancellationToken);
        await client.TransferMoneyAsync(loose.MoneyAssetId, otherLoose.MoneyAssetId, 100m, FundingDay, cancellationToken);
        await client.DepositMoneyAsync(first.MoneyAssetId, 300m, FundingDay, cancellationToken);
        await client.WithdrawMoneyAsync(second.MoneyAssetId, 50m, FundingDay, cancellationToken);

        var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);
        Assert.Empty(month.Expenses.GroupDeposits ?? []);
        var contribution = Assert.Single(month.Expenses.GroupContributions!, c => c.GroupId == groupId);
        Assert.Equal((0m, "Unsettled"), (contribution.FundedPln, contribution.Status));
    }

    [Fact]
    public async Task RealisesInPastMonths()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateIncomeAccountAsync(cancellationToken, "A");
        var groupId = await client.CreateFundingGroupAsync(
            cancellationToken, "Emerytura", 1_000m, contributions: [new ContributionBody(1_000m, a.AccountId)]);
        var target = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Emerytalne");
        await client.TransferMoneyAsync(a.MoneyAssetId, target.MoneyAssetId, 1_000m, FundingDay, cancellationToken);
        Factory.Clock.SetUtcNow(AfterDueDayUtc);

        var october = await client.ContributionOfAsync(groupId, cancellationToken);
        var december = (await client.GetPlanningMonthAsync("2026-12", cancellationToken)).Expenses;

        Assert.Equal((1_000m, "Settled"), (october.FundedPln, october.Status));
        Assert.All(december.GroupContributions ?? [], c => Assert.Equal((null, null), (c.FundedPln, c.Status)));
        Assert.Empty(december.GroupDeposits ?? []);
    }

    [Fact]
    public async Task SettlementAccountIsIncomeAccountInActual()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "A");
        var c = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "C", balance: 5_000m);
        var incomeId = await client.CreateOneOffIncomeAsync(a.AccountId, cancellationToken, "Premia", 5_000m, new DateOnly(2026, 10, 12));
        await client.SettleOneOffIncomeAsync(incomeId, c.AccountId, 5_000m, new DateOnly(2026, 10, 12), cancellationToken);
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Poduszka", 0m);
        var target = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Poduszka PLN");
        var baseline = (await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).FreeFunds.ActualPln;

        await client.TransferMoneyAsync(c.MoneyAssetId, target.MoneyAssetId, 2_000m, FundingDay, cancellationToken);

        var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);
        var deposit = Assert.Single(month.Expenses.GroupDeposits!);
        Assert.Equal((2_000m, 2_000m), (deposit.FromIncomePln, deposit.ExcessPln));
        Assert.Equal(baseline - 2_000m, month.FreeFunds.ActualPln);
    }

    [Fact]
    public async Task IgnoresDividendsAndOtherMonths()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateIncomeAccountAsync(cancellationToken, "A");
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Emerytura", 1_000m);
        var broker = await client.CreateBrokerageInGroupAsync(cancellationToken, groupId, null, balance: 900m);

        await client.TransferMoneyAsync(a.MoneyAssetId, broker.CashAssetId, 700m, EarlierMonthDay, cancellationToken);
        await client.RecordBrokerageTradeAsync(
            broker.EtfAssetId, cancellationToken, TransactionType.Dividend, quantity: 50m, unitPrice: 1m, date: LaterFundingDay);

        var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);
        Assert.Empty(month.Expenses.GroupDeposits ?? []);
        Assert.Equal(0m, Assert.Single(month.Expenses.GroupContributions!).FundedPln);
    }

    [Fact]
    public async Task StatusFollowsFunding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var source = await client.CreateIncomeAccountAsync(cancellationToken, "Źródło", balance: 10_000m);
        var partial = await client.CreateFundingGroupAsync(cancellationToken, "Emerytura", 1_000m, color: 1);
        var settled = await client.CreateFundingGroupAsync(cancellationToken, "Dom", 1_000m, color: 2);
        var unsettled = await client.CreateFundingGroupAsync(cancellationToken, "Wakacje", 1_000m, color: 3);
        var noPlan = await client.CreateFundingGroupAsync(cancellationToken, "Bez planu", 0m, color: 4);
        var partialAccount = await client.CreateBankAccountInGroupAsync(cancellationToken, partial, "A");
        var settledAccount = await client.CreateBankAccountInGroupAsync(cancellationToken, settled, "B");
        await client.CreateBankAccountInGroupAsync(cancellationToken, unsettled, "C");
        var noPlanAccount = await client.CreateBankAccountInGroupAsync(cancellationToken, noPlan, "D");
        await client.TransferMoneyAsync(source.MoneyAssetId, partialAccount.MoneyAssetId, 600m, FundingDay, cancellationToken);
        await client.TransferMoneyAsync(source.MoneyAssetId, settledAccount.MoneyAssetId, 1_500m, FundingDay, cancellationToken);
        await client.TransferMoneyAsync(source.MoneyAssetId, noPlanAccount.MoneyAssetId, 200m, FundingDay, cancellationToken);

        var contributions = (await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).Expenses.GroupContributions!;

        Assert.Equal(3, contributions.Count);
        Assert.DoesNotContain(contributions, c => c.GroupId == noPlan);
        Assert.Equal((600m, "Partial"), Of(contributions, partial));
        Assert.Equal((1_000m, "Settled"), Of(contributions, settled));
        Assert.Equal((0m, "Unsettled"), Of(contributions, unsettled));
    }

    [Fact]
    public async Task UsesFrozenRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        Factory.FxRateLookupClient.WithRate("EUR", EarlierMonthDay, 4.25m);
        Factory.FxRateLookupClient.WithRate("EUR", FundingDay, 4.25m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Emerytura", 0m);
        var source = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "Źródło EUR", balance: 200m, currency: "EUR");
        var euro = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Revolut EUR", currency: "EUR");
        await client.TransferMoneyAsync(source.MoneyAssetId, euro.MoneyAssetId, 100m, FundingDay, cancellationToken);
        Factory.FxRateLookupClient.WithRate("EUR", FundingDay, 9m);
        Factory.InstrumentQuoteLookupClient.WithUnavailable();

        var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);

        Assert.Equal(425m, Assert.Single(month.Expenses.GroupDeposits!).TotalPln);
        Assert.False(month.FreeFunds.Incomplete);
    }

    [Fact]
    public async Task MissingRateMarksIncomplete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        Factory.FxRateLookupClient.WithRate("EUR", EarlierMonthDay, 4.25m);
        Factory.FxRateLookupClient.WithRate("EUR", FundingDay, 4.25m);
        using var client = Factory.CreateAuthenticatedClient(userId);
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Emerytura", 0m);
        var euroSource = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "Źródło EUR", balance: 200m, currency: "EUR");
        var plnSource = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "Źródło PLN", balance: 200m);
        var euro = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Revolut EUR", currency: "EUR");
        var pln = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "mBank");
        await client.TransferMoneyAsync(euroSource.MoneyAssetId, euro.MoneyAssetId, 100m, FundingDay, cancellationToken);
        await client.TransferMoneyAsync(plnSource.MoneyAssetId, pln.MoneyAssetId, 100m, FundingDay, cancellationToken);
        await using (var dbContext = CreateDbContext(userId))
        {
            var euroLegs = await dbContext.Transactions
                .Where(t => t.Date == FundingDay && (t.AssetId == euroSource.MoneyAssetId || t.AssetId == euro.MoneyAssetId))
                .ToListAsync(cancellationToken);
            Assert.Equal(2, euroLegs.Count);
            euroLegs.ForEach(t => t.FxRateToPln = null);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);

        Assert.Equal(100m, Assert.Single(month.Expenses.GroupDeposits!).TotalPln);
        Assert.True(month.FreeFunds.Incomplete);
    }

    private static (decimal?, string?) Of(IEnumerable<GroupContributionView> contributions, Guid groupId)
    {
        var contribution = contributions.Single(c => c.GroupId == groupId);

        return (contribution.FundedPln, contribution.Status);
    }
}
