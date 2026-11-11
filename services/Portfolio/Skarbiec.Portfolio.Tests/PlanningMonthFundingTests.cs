using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.GroupFundingApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningMonthFundingTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task InternalMoveIsNotCountedTwice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Emerytura", 1_000m);
        var broker = await client.CreateBrokerageInGroupAsync(cancellationToken, groupId, groupId);

        await client.DepositMoneyAsync(broker.CashAssetId, 600m, FundingDay, cancellationToken);
        await client.RecordBrokerageTradeAsync(
            broker.EtfAssetId, cancellationToken, quantity: 6m, unitPrice: 100m, date: LaterFundingDay);

        var contribution = await client.ContributionOfAsync(groupId, cancellationToken);
        Assert.Equal(600m, contribution.FundedPln);
    }

    [Fact]
    public async Task CountsExternalInflowAndOutflow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Dom", 1_000m);
        var otherGroupId = await client.CreateFundingGroupAsync(cancellationToken, "Wakacje", 500m, color: 2);
        var main = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "Główne", balance: 5_000m);
        var home = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Rezerwa");
        var holidays = await client.CreateBankAccountInGroupAsync(cancellationToken, otherGroupId, "Urlopowe", balance: 1_000m);
        var broker = await client.CreateBrokerageInGroupAsync(cancellationToken, null, groupId, balance: 1_000m);

        await client.TransferMoneyAsync(main.MoneyAssetId, home.MoneyAssetId, 400m, FundingDay, cancellationToken);
        await client.TransferMoneyAsync(holidays.MoneyAssetId, home.MoneyAssetId, 300m, FundingDay, cancellationToken);
        await client.WithdrawMoneyAsync(home.MoneyAssetId, 150m, FundingDay, cancellationToken);
        await client.RecordBrokerageTradeAsync(
            broker.EtfAssetId, cancellationToken, quantity: 2m, unitPrice: 100m, date: FundingDay);
        await client.TransferMoneyAsync(home.MoneyAssetId, main.MoneyAssetId, 50m, LaterFundingDay, cancellationToken);

        var home700 = await client.ContributionOfAsync(groupId, cancellationToken);
        var holidaysDrained = await client.ContributionOfAsync(otherGroupId, cancellationToken);
        Assert.Equal(700m, home700.FundedPln);
        Assert.Equal(0m, holidaysDrained.FundedPln);
    }

    [Fact]
    public async Task NegativeNetIsZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Dom", 500m);
        var home = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Rezerwa", balance: 1_000m);

        await client.WithdrawMoneyAsync(home.MoneyAssetId, 300m, FundingDay, cancellationToken);

        var contribution = await client.ContributionOfAsync(groupId, cancellationToken);
        Assert.Equal((0m, "Unsettled"), (contribution.FundedPln, contribution.Status));
    }

    [Fact]
    public async Task IgnoresDividendsAndOtherMonths()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Emerytura", 1_000m);
        var broker = await client.CreateBrokerageInGroupAsync(cancellationToken, groupId, null, balance: 900m);

        await client.RecordBrokerageTradeAsync(
            broker.EtfAssetId, cancellationToken, quantity: 5m, unitPrice: 100m, date: FundingDay);
        await client.RecordBrokerageTradeAsync(
            broker.EtfAssetId, cancellationToken, TransactionType.Dividend, quantity: 50m, unitPrice: 1m, date: LaterFundingDay);
        await client.DepositMoneyAsync(broker.CashAssetId, 800m, FundingDay, cancellationToken);

        var contribution = await client.ContributionOfAsync(groupId, cancellationToken);
        Assert.Equal(300m, contribution.FundedPln);
    }

    [Fact]
    public async Task StatusFollowsFunding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var sourceA = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "Źródło A");
        var sourceB = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "Źródło B");
        var partial = await client.CreateFundingGroupAsync(
            cancellationToken, "Emerytura", 1_000m, color: 1, contributionAccountIds: [sourceA.AccountId, sourceB.AccountId]);
        var settled = await client.CreateFundingGroupAsync(cancellationToken, "Dom", 1_000m, color: 2);
        var unsettled = await client.CreateFundingGroupAsync(cancellationToken, "Wakacje", 1_000m, color: 3);
        var noPlan = await client.CreateFundingGroupAsync(cancellationToken, "Bez planu", 0m, color: 4);
        var partialAccount = await client.CreateBankAccountInGroupAsync(cancellationToken, partial, "A");
        var settledAccount = await client.CreateBankAccountInGroupAsync(cancellationToken, settled, "B");
        await client.CreateBankAccountInGroupAsync(cancellationToken, unsettled, "C");
        var noPlanAccount = await client.CreateBankAccountInGroupAsync(cancellationToken, noPlan, "D");
        await client.DepositMoneyAsync(partialAccount.MoneyAssetId, 600m, FundingDay, cancellationToken);
        await client.DepositMoneyAsync(settledAccount.MoneyAssetId, 1_500m, FundingDay, cancellationToken);
        await client.DepositMoneyAsync(noPlanAccount.MoneyAssetId, 200m, FundingDay, cancellationToken);

        var contributions = (await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).Expenses.GroupContributions!;

        Assert.Equal(3, contributions.Count);
        Assert.DoesNotContain(contributions, c => c.GroupId == noPlan);
        Assert.Equal((600m, "Partial"), Of(contributions, partial));
        Assert.Equal((1_500m, "Settled"), Of(contributions, settled));
        Assert.Equal((0m, "Unsettled"), Of(contributions, unsettled));
    }

    [Fact]
    public async Task NoStatusOutsideCurrentMonth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Emerytura", 1_000m);
        var account = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "A");
        await client.DepositMoneyAsync(account.MoneyAssetId, 600m, FundingDay, cancellationToken);

        var october = await client.ContributionOfAsync(groupId, cancellationToken);
        var november = await client.ContributionOfAsync(groupId, cancellationToken, "2026-11");
        var september = (await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Expenses.GroupContributions ?? [];

        Assert.Equal((600m, "Partial"), (october.FundedPln, october.Status));
        Assert.Equal((null, null), (november.FundedPln, november.Status));
        Assert.All(september, c => Assert.Equal((null, null), (c.FundedPln, c.Status)));
    }

    [Fact]
    public async Task UsesFrozenRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        Factory.FxRateLookupClient.WithRate("EUR", FundingDay, 4.25m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Emerytura", 1_000m);
        var euro = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Revolut EUR", currency: "EUR");
        await client.DepositMoneyAsync(euro.MoneyAssetId, 100m, FundingDay, cancellationToken);
        Factory.FxRateLookupClient.WithRate("EUR", FundingDay, 9m);
        Factory.InstrumentQuoteLookupClient.WithUnavailable();

        var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);

        Assert.Equal(425m, Assert.Single(month.Expenses.GroupContributions!).FundedPln);
        Assert.False(month.FreeFunds.Incomplete);
    }

    [Fact]
    public async Task MissingRateMarksIncomplete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        Factory.FxRateLookupClient.WithRate("EUR", FundingDay, 4.25m);
        using var client = Factory.CreateAuthenticatedClient(userId);
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Emerytura", 1_000m);
        var euro = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Revolut EUR", currency: "EUR");
        var pln = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "mBank");
        var euroDeposit = await client.DepositMoneyAsync(euro.MoneyAssetId, 100m, FundingDay, cancellationToken);
        await client.DepositMoneyAsync(pln.MoneyAssetId, 100m, FundingDay, cancellationToken);
        await using (var dbContext = CreateDbContext(userId))
        {
            var transaction = await dbContext.Transactions.SingleAsync(t => t.Id == euroDeposit, cancellationToken);
            transaction.FxRateToPln = null;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);

        Assert.Equal(100m, Assert.Single(month.Expenses.GroupContributions!).FundedPln);
        Assert.True(month.FreeFunds.Incomplete);
    }

    private static (decimal?, string?) Of(IEnumerable<GroupContributionView> contributions, Guid groupId)
    {
        var contribution = contributions.Single(c => c.GroupId == groupId);

        return (contribution.FundedPln, contribution.Status);
    }
}
