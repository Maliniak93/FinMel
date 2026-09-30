using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.SavingsAccounts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// savings-accounts AC-9: a <see cref="SavingsAccount"/> is user-owned and nested under a portfolio, so
/// the flat tenancy template does not fit — the same facts are written by hand, for both the owner's
/// portfolio id and the stranger's own ("sneaky path"), plus the cross-portfolio list. Every
/// single-resource call is a 404 (never 403), and nothing of the owner's changes.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class SavingsAccountTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ByStranger_DoesNotIncludeOwnersAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await owner.CreatePortfolioWithSavingsAccountAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await stranger.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewSavingsAccountRequest(name: "Stranger's own"));

        var response = await stranger.GetAsync(AllSavingsAccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = Assert.Single((await response.Content.ReadFromJsonAsync<List<SavingsAccountResponse>>(cancellationToken))!);
        Assert.Equal("Stranger's own", listed.Name);
    }

    [Fact]
    public async Task Get_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerPortfolioId, account) = await owner.CreatePortfolioWithSavingsAccountAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);

        var viaOwnersPortfolio = await stranger.GetAsync(SavingsAccountUri(ownerPortfolioId, account.AssetId), cancellationToken);
        var viaStrangersPortfolio = await stranger.GetAsync(SavingsAccountUri(strangerPortfolioId, account.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, viaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaStrangersPortfolio.StatusCode);
    }

    [Fact]
    public async Task Put_ByStranger_ReturnsNotFoundAndLeavesAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerPortfolioId, account) = await owner.CreatePortfolioWithSavingsAccountAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);
        var payload = NewUpdateSavingsAccountRequest(name: "Stranger's edit");

        var viaOwnersPortfolio = await stranger.PutAsJsonAsync(SavingsAccountUri(ownerPortfolioId, account.AssetId), payload, cancellationToken);
        var viaStrangersPortfolio = await stranger.PutAsJsonAsync(SavingsAccountUri(strangerPortfolioId, account.AssetId), payload, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, viaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaStrangersPortfolio.StatusCode);
        var unchanged = await owner.GetSavingsAccountAsync(ownerPortfolioId, account.AssetId, cancellationToken);
        Assert.Equal("Savings account", unchanged.Name);
        Assert.Equal(5.25m, unchanged.AnnualInterestRatePercent);
        Assert.Equal(10_000m, unchanged.Balance);
    }

    /// <summary>A stranger cannot add an account into the owner's portfolio — 404, and no row lands under either user.</summary>
    [Fact]
    public async Task Add_IntoStrangersPortfolio_ReturnsNotFoundAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var ownerPortfolioId = await owner.CreatePortfolioAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.PostAsJsonAsync(SavingsAccountsUri(ownerPortfolioId), NewSavingsAccountRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var dbContext = CreateDbContext(ownerId);
        Assert.Equal(0, await dbContext.Set<SavingsAccount>().IgnoreQueryFilters().CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Assets.IgnoreQueryFilters().CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Transactions.IgnoreQueryFilters().CountAsync(cancellationToken));
    }

    /// <summary>The terms row itself is tenant-filtered: another user's context never sees it.</summary>
    [Fact]
    public async Task SavingsAccount_QueriedAsStranger_IsFilteredOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var (_, account) = await owner.CreatePortfolioWithSavingsAccountAsync(cancellationToken);

        await using var strangerDb = CreateDbContext(Guid.NewGuid());
        Assert.False(await strangerDb.Set<SavingsAccount>().AnyAsync(t => t.AssetId == account.AssetId, cancellationToken));
        await using var ownerDb = CreateDbContext(ownerId);
        Assert.True(await ownerDb.Set<SavingsAccount>().AnyAsync(t => t.AssetId == account.AssetId, cancellationToken));
    }

    /// <summary>
    /// savings-interest-settlement AC-11: a stranger previews, settles or undoes on the owner's account
    /// through the owner's portfolio id and through their own - 404 every time, and nothing changes.
    /// </summary>
    [Fact]
    public async Task Interest_ForeignAccount_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(OctoberEndedUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var (ownerPortfolioId, account) = await owner.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        await owner.SettlePreviewedSavingsInterestAsync(ownerPortfolioId, account.AssetId, cancellationToken);
        var settlementId = await owner.GetLastSettlementIdAsync(ownerPortfolioId, account.AssetId, cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);

        foreach (var portfolioId in new[] { ownerPortfolioId, strangerPortfolioId })
        {
            var preview = await stranger.GetAsync(SavingsInterestPreviewUri(portfolioId, account.AssetId), cancellationToken);
            var settle = await stranger.PostAsJsonAsync(
                SavingsInterestSettlementsUri(portfolioId, account.AssetId),
                NewSettleInterestBody(OctoberEnd, 42.61m, 8.10m),
                cancellationToken);
            var undo = await stranger.DeleteAsync(
                SavingsInterestSettlementUri(portfolioId, account.AssetId, settlementId), cancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, settle.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, undo.StatusCode);
        }

        Assert.Equal(1, await CountSavingsSettlementsAsync(ownerId, cancellationToken, account.AssetId));
        var unchanged = await owner.GetSavingsAccountAsync(ownerPortfolioId, account.AssetId, cancellationToken);
        Assert.Equal(10_033.29m, unchanged.Balance);
        Assert.Equal(settlementId, unchanged.LastSettlement!.SettlementId);
    }

    /// <summary>The settlement row is tenant-filtered: another user's context never sees it.</summary>
    [Fact]
    public async Task InterestSettlement_QueriedAsStranger_IsFilteredOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var (portfolioId, account) = await owner.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        await owner.SettlePreviewedSavingsInterestAsync(portfolioId, account.AssetId, cancellationToken);

        Assert.Equal(0, await CountSavingsSettlementsAsync(Guid.NewGuid(), cancellationToken));
        Assert.Equal(1, await CountSavingsSettlementsAsync(ownerId, cancellationToken));
    }
}
