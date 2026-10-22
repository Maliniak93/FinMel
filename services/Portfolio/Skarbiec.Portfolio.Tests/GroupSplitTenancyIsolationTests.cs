using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioAssertions;

namespace Skarbiec.Portfolio.Tests;

public sealed class GroupSplitTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task GetMonth_AsStranger_ShowsNoneOfTheOwnersGroupsContributionsOrValuation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var ownerGroups = await owner.CreateSplitGroupsAsync(cancellationToken);
        await SeedValuationReplicaAsync(
            ownerId, cancellationToken, (ownerGroups.CushionId, [(AssetClass.BankAccount, 11_000m)]));

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await stranger.CreateFreeFundsOf7000Async(cancellationToken);
        var own = await stranger.CreateAssetGroupAsync(cancellationToken, "Moja", ColorKey(3), monthlyContributionPln: 500m);

        var month = await stranger.GetPlanningMonthAsync("2026-10", cancellationToken);

        var contribution = Assert.Single(Present(month.Expenses.GroupContributions));
        Assert.Equal((own.Id, 500m), (contribution.GroupId, contribution.AmountPln));
        Assert.Equal(1_500m, month.Expenses.CostPln);
        var split = Present(month.Split);
        Assert.Null(split.Cushion);
        Assert.True(split.ValuationMissing);
        Assert.Equal(own.Id, Assert.Single(split.Groups).GroupId);
    }

    [Fact]
    public async Task SaveShares_WithTheOwnersGroups_ReturnsMismatchAndKeepsTheOwnersShares()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var groups = await owner.CreateSplitGroupsAsync(cancellationToken);
        await owner.SaveGroupSharesAsync(cancellationToken, (groups.RetirementId, 70m), (groups.InvestingId, 30m));

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var mine = await stranger.CreateAssetGroupAsync(cancellationToken, "Moja", ColorKey(3));
        var response = await stranger.PutAsJsonAsync(
            SharesUri, NewSharesBody((groups.RetirementId, 70m), (groups.InvestingId, 30m)), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, SharesGroupMismatchErrorCode, cancellationToken);
        var ownerShares = await owner.ReadSharesAsync(cancellationToken);
        Assert.Equal(70m, ownerShares[groups.RetirementId]);
        Assert.Equal(30m, ownerShares[groups.InvestingId]);
        Assert.Equal(100m, (await stranger.ReadSharesAsync(cancellationToken))[mine.Id]);
    }
}
