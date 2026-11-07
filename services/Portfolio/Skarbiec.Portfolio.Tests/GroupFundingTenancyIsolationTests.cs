using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
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
        var ownerGroup = await owner.CreateFundingGroupAsync(cancellationToken, "Emerytura", 1_000m);
        var ownerAccount = await owner.CreateBankAccountInGroupAsync(cancellationToken, ownerGroup, "Główne");
        await owner.DepositMoneyAsync(ownerAccount.MoneyAssetId, 600m, FundingDay, cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerGroup = await stranger.CreateFundingGroupAsync(cancellationToken, "Emerytura", 1_000m);

        var own = await owner.ContributionOfAsync(ownerGroup, cancellationToken);
        var other = await stranger.ContributionOfAsync(strangerGroup, cancellationToken);
        Assert.Equal((600m, "Partial"), (own.FundedPln, own.Status));
        Assert.Equal((0m, "Unsettled"), (other.FundedPln, other.Status));
    }
}
