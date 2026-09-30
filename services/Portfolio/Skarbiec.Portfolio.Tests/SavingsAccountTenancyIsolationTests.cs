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
}
