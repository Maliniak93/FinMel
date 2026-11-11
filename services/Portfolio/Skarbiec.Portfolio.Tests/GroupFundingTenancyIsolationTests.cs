using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.GroupFundingApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class GroupFundingTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task OtherUsersTransactionsDoNotCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ownerSource = await owner.CreateIncomeAccountAsync(cancellationToken);
        var ownerGroup = await owner.CreateFundingGroupAsync(cancellationToken, "Emerytura", 1_000m);
        var ownerAccount = await owner.CreateBankAccountInGroupAsync(cancellationToken, ownerGroup, "Główne");
        await owner.TransferMoneyAsync(ownerSource.MoneyAssetId, ownerAccount.MoneyAssetId, 600m, FundingDay, cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerSource = await stranger.CreateIncomeAccountAsync(cancellationToken);
        var strangerGroup = await stranger.CreateFundingGroupAsync(cancellationToken, "Emerytura", 1_000m);
        var strangerAccount = await stranger.CreateBankAccountInGroupAsync(cancellationToken, strangerGroup, "Główne");
        await stranger.TransferMoneyAsync(strangerSource.MoneyAssetId, strangerAccount.MoneyAssetId, 900m, FundingDay, cancellationToken);

        var own = await owner.ContributionOfAsync(ownerGroup, cancellationToken);
        var contributions = (await owner.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).Expenses.GroupContributions;
        Assert.Equal((600m, "Partial"), (own.NetPln, own.Status));
        Assert.DoesNotContain(contributions!, c => c.GroupId == strangerGroup);
    }

    [Fact]
    public async Task IncomeAccountsAreOwnOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var salary = await owner.CreateBankAccountAsync(cancellationToken, "Pensja");
        var savings = await owner.CreateBankAccountAsync(cancellationToken, "Oszczędnościowe");
        await owner.CreateRecurringIncomeAsync(salary.AccountId, cancellationToken, "Wypłata", 8_000m, dayOfMonth: 10);
        var groupId = await owner.CreateFundingGroupAsync(
            cancellationToken, "Emerytura", 1_000m,
            contributions: [new AssetGroupsApi.ContributionBody(1_000m, savings.AccountId)]);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerAccount = await stranger.CreateBankAccountAsync(cancellationToken, "Pensja");
        await stranger.CreateRecurringIncomeAsync(strangerAccount.AccountId, cancellationToken, "Wypłata", 9_000m, dayOfMonth: 10);
        await stranger.CreateOneOffIncomeAsync(strangerAccount.AccountId, cancellationToken, "Premia", 1_000m, FundingDay);

        var month = await owner.GetPlanningMonthAsync(CurrentMonth, cancellationToken);

        var row = Assert.Single(month.Expenses.GroupContributions!, c => c.GroupId == groupId);
        Assert.Equal((1_000m, 0m), (row.AmountPln, row.FromIncomePln));
        Assert.Equal(8_000m, month.FreeFunds.PlanPln);
    }
}
