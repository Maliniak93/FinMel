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
        Assert.Equal((4_000m, "Settled"), (contribution.NetPln, contribution.Status));
        Assert.Equal(baseline - 4_000m, month.FreeFunds.ActualPln);
    }

    [Fact]
    public async Task NetIsNotCappedByPlan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateIncomeAccountAsync(cancellationToken, "A");
        var groupId = await client.CreateFundingGroupAsync(
            cancellationToken, "Emerytura", 2_000m, contributions: [new ContributionBody(2_000m, a.AccountId)]);
        var target = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Emerytalne");

        await client.TransferMoneyAsync(a.MoneyAssetId, target.MoneyAssetId, 4_000m, FundingDay, cancellationToken);

        var contribution = await client.ContributionOfAsync(groupId, cancellationToken);
        Assert.Equal((4_000m, "Settled"), (contribution.NetPln, contribution.Status));
    }

    [Fact]
    public async Task NetSubtractsOutflowsAndCountsPlainDeposits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateIncomeAccountAsync(cancellationToken, "A");
        var source = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "Źródło", balance: 10_000m);
        var outside = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "Na zewnątrz");
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Poduszka", 0m);
        var target = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Oszczędnościowe", balance: 5_000m);

        await client.WithdrawMoneyAsync(target.MoneyAssetId, 233.14m, FundingDay, cancellationToken);
        await client.TransferMoneyAsync(source.MoneyAssetId, target.MoneyAssetId, 3_200m, FundingDay, cancellationToken);
        await client.TransferMoneyAsync(target.MoneyAssetId, outside.MoneyAssetId, 2_000m, FundingDay, cancellationToken);
        await client.TransferMoneyAsync(source.MoneyAssetId, target.MoneyAssetId, 3_000m, FundingDay, cancellationToken);

        var contribution = await client.ContributionOfAsync(groupId, cancellationToken);
        Assert.Equal((3_966.86m, "Settled"), (contribution.NetPln, contribution.Status));
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

        var contribution = await client.ContributionOfAsync(groupId, cancellationToken);
        Assert.Equal((0m, "Unsettled"), (contribution.NetPln, contribution.Status));
    }

    [Fact]
    public async Task GroupWithoutPlanAndNoFlowHasNoItem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateIncomeAccountAsync(cancellationToken, "A");
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Bez planu", 0m);
        await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Konto", balance: 1_000m);

        var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);

        Assert.DoesNotContain(month.Expenses.GroupContributions ?? [], c => c.GroupId == groupId);
    }

    [Fact]
    public async Task TradeCashFromOutsideCounts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateIncomeAccountAsync(cancellationToken, "A");
        var outsideGroup = await client.CreateFundingGroupAsync(cancellationToken, "Akcje", 0m, color: 1);
        var sameGroup = await client.CreateFundingGroupAsync(cancellationToken, "Maklerskie razem", 0m, color: 2);
        var outsideCash = await client.CreateBrokerageInGroupAsync(
            cancellationToken, null, outsideGroup, name: "Maklerskie 1", balance: 5_000m);
        var insideCash = await client.CreateBrokerageInGroupAsync(
            cancellationToken, sameGroup, sameGroup, name: "Maklerskie 2", balance: 5_000m);

        await client.RecordBrokerageTradeAsync(
            outsideCash.EtfAssetId, cancellationToken, quantity: 10m, unitPrice: 120m, date: FundingDay);
        await client.RecordBrokerageTradeAsync(
            insideCash.EtfAssetId, cancellationToken, quantity: 10m, unitPrice: 120m, date: FundingDay);

        var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);
        Assert.Equal(1_200m, Assert.Single(month.Expenses.GroupContributions!, c => c.GroupId == outsideGroup).NetPln);
        Assert.DoesNotContain(month.Expenses.GroupContributions!, c => c.GroupId == sameGroup);
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

        Assert.Equal((1_000m, "Settled"), (october.NetPln, october.Status));
        Assert.All(december.GroupContributions ?? [], c => Assert.Equal((null, null), (c.NetPln, c.Status)));
    }

    [Fact]
    public async Task ActualSubtractsNetOfAllGroups()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var source = await client.CreateIncomeAccountAsync(cancellationToken, "A", balance: 20_000m);
        var emerytura = await client.CreateFundingGroupAsync(cancellationToken, "Emerytura", 2_000m, color: 1);
        var poduszka = await client.CreateFundingGroupAsync(cancellationToken, "Poduszka", 0m, color: 2);
        var other = await client.CreateBankAccountInGroupAsync(cancellationToken, null, "Inne", balance: 10_000m);
        var emeryturaAccount = await client.CreateBankAccountInGroupAsync(cancellationToken, emerytura, "E");
        var poduszkaAccount = await client.CreateBankAccountInGroupAsync(cancellationToken, poduszka, "P");
        var baseline = (await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).FreeFunds.ActualPln;

        await client.TransferMoneyAsync(source.MoneyAssetId, emeryturaAccount.MoneyAssetId, 4_000m, FundingDay, cancellationToken);
        await client.TransferMoneyAsync(other.MoneyAssetId, poduszkaAccount.MoneyAssetId, 3_966.86m, FundingDay, cancellationToken);

        var month = await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken);
        Assert.Equal(baseline - 7_966.86m, month.FreeFunds.ActualPln);
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

        var contribution = await client.ContributionOfAsync(groupId, cancellationToken);
        Assert.Equal(0m, contribution.NetPln);
    }

    [Fact]
    public async Task StatusFollowsNet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var source = await client.CreateIncomeAccountAsync(cancellationToken, "Źródło", balance: 10_000m);
        var partial = await client.CreateFundingGroupAsync(cancellationToken, "Emerytura", 2_000m, color: 1);
        var negative = await client.CreateFundingGroupAsync(cancellationToken, "Dom", 2_000m, color: 2);
        var partialAccount = await client.CreateBankAccountInGroupAsync(cancellationToken, partial, "A");
        var negativeAccount = await client.CreateBankAccountInGroupAsync(cancellationToken, negative, "B", balance: 5_000m);
        await client.TransferMoneyAsync(source.MoneyAssetId, partialAccount.MoneyAssetId, 1_000m, FundingDay, cancellationToken);
        await client.DepositMoneyAsync(negativeAccount.MoneyAssetId, 1_000m, FundingDay, cancellationToken);
        await client.WithdrawMoneyAsync(negativeAccount.MoneyAssetId, 3_000m, FundingDay, cancellationToken);

        var contributions = (await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).Expenses.GroupContributions!;

        Assert.Equal((1_000m, "Partial"), Of(contributions, partial));
        Assert.Equal((-2_000m, "Unsettled"), Of(contributions, negative));
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

        Assert.Equal(425m, Assert.Single(month.Expenses.GroupContributions!, c => c.GroupId == groupId).NetPln);
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

        Assert.Equal(100m, Assert.Single(month.Expenses.GroupContributions!, c => c.GroupId == groupId).NetPln);
        Assert.True(month.FreeFunds.Incomplete);
    }

    private static (decimal?, string?) Of(IEnumerable<GroupContributionView> contributions, Guid groupId)
    {
        var contribution = contributions.Single(c => c.GroupId == groupId);

        return (contribution.NetPln, contribution.Status);
    }
}
