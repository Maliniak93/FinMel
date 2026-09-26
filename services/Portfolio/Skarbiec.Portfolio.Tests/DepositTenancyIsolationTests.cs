using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// term-deposits AC-13: a <see cref="TermDeposit"/> is user-owned and nested under a portfolio, so the
/// flat <see cref="Skarbiec.Testing.Tenancy.TenancyIsolationTests{TProgram}"/> template does not fit —
/// the same facts are written by hand, for both the owner's portfolio id and the stranger's own
/// ("sneaky path"), plus the cross-portfolio list. Every single-resource call is a 404 (never 403),
/// and nothing of the owner's changes.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class DepositTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ByStranger_DoesNotIncludeOwnersDeposit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await owner.CreatePortfolioWithDepositAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await stranger.CreatePortfolioWithDepositAsync(cancellationToken, NewDepositRequest(name: "Stranger's own"));

        var response = await stranger.GetAsync(AllDepositsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = Assert.Single((await response.Content.ReadFromJsonAsync<List<DepositResponse>>(cancellationToken))!);
        Assert.Equal("Stranger's own", listed.Name);
    }

    [Fact]
    public async Task Get_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerPortfolioId, deposit) = await owner.CreatePortfolioWithDepositAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);

        var viaOwnersPortfolio = await stranger.GetAsync(DepositUri(ownerPortfolioId, deposit.AssetId), cancellationToken);
        var viaStrangersPortfolio = await stranger.GetAsync(DepositUri(strangerPortfolioId, deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, viaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaStrangersPortfolio.StatusCode);
    }

    [Fact]
    public async Task Put_ByStranger_ReturnsNotFoundAndLeavesDeposit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var (ownerPortfolioId, deposit) = await owner.CreatePortfolioWithDepositAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);
        var payload = NewDepositRequest(name: "Stranger's edit", principal: 1m).ToUpdateRequest();

        var viaOwnersPortfolio = await stranger.PutAsJsonAsync(DepositUri(ownerPortfolioId, deposit.AssetId), payload, cancellationToken);
        var viaStrangersPortfolio = await stranger.PutAsJsonAsync(DepositUri(strangerPortfolioId, deposit.AssetId), payload, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, viaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaStrangersPortfolio.StatusCode);
        var asset = await owner.GetAssetAsync(ownerPortfolioId, deposit.AssetId, cancellationToken);
        Assert.Equal("Term deposit", asset.Name);
        Assert.Equal(10_000m, asset.Quantity);
        await using var dbContext = CreateDbContext(ownerId);
        Assert.Equal(10_000m, (await dbContext.Set<TermDeposit>().SingleAsync(t => t.AssetId == deposit.AssetId, cancellationToken)).Principal);
    }

    [Fact]
    public async Task Delete_ByStranger_ReturnsNotFoundAndLeavesDeposit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var (ownerPortfolioId, deposit) = await owner.CreatePortfolioWithDepositAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);

        var viaOwnersPortfolio = await stranger.DeleteAsync(AssetUri(ownerPortfolioId, deposit.AssetId), cancellationToken);
        var viaStrangersPortfolio = await stranger.DeleteAsync(AssetUri(strangerPortfolioId, deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, viaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, viaStrangersPortfolio.StatusCode);
        var stillThere = await owner.GetAsync(DepositUri(ownerPortfolioId, deposit.AssetId), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
        await using var dbContext = CreateDbContext(ownerId);
        Assert.True(await dbContext.Set<TermDeposit>().AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
        Assert.Equal(1, await dbContext.Transactions.CountAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
    }

    /// <summary>
    /// term-deposits-settlement AC-6: a stranger previewing or settling the owner's Due deposit — via
    /// the owner's portfolio id or their own — gets 404 every time, and the deposit stays unsettled.
    /// </summary>
    [Fact]
    public async Task Settle_ForeignDeposit_IsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var (ownerPortfolioId, deposit) = await owner.CreatePortfolioWithDepositAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);

        var previewViaOwnersPortfolio = await stranger.GetAsync(
            DepositSettlementPreviewUri(ownerPortfolioId, deposit.AssetId), cancellationToken);
        var previewViaStrangersPortfolio = await stranger.GetAsync(
            DepositSettlementPreviewUri(strangerPortfolioId, deposit.AssetId), cancellationToken);
        var settleViaOwnersPortfolio = await stranger.PostAsJsonAsync(
            SettleDepositUri(ownerPortfolioId, deposit.AssetId), NewSettleRequest(), cancellationToken);
        var settleViaStrangersPortfolio = await stranger.PostAsJsonAsync(
            SettleDepositUri(strangerPortfolioId, deposit.AssetId), NewSettleRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, previewViaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, previewViaStrangersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, settleViaOwnersPortfolio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, settleViaStrangersPortfolio.StatusCode);
        await owner.AssertDepositUnsettledAsync(ownerPortfolioId, deposit.AssetId, cancellationToken);
        await using var dbContext = CreateDbContext(ownerId);
        var terms = await dbContext.Set<TermDeposit>().SingleAsync(t => t.AssetId == deposit.AssetId, cancellationToken);
        Assert.Null(terms.SettledOn);
        Assert.Null(terms.SettledGrossInterest);
        Assert.Null(terms.SettledTax);
        // Control: the owner can still settle it — the 404s were about the caller, not the deposit.
        var ownSettle = await owner.PostAsJsonAsync(SettleDepositUri(ownerPortfolioId, deposit.AssetId), NewSettleRequest(), cancellationToken);
        Assert.True(ownSettle.IsSuccessStatusCode, $"The owner's settle answered {(int)ownSettle.StatusCode}.");
    }

    /// <summary>A stranger cannot add a deposit into the owner's portfolio — 404, and no row lands under either user.</summary>
    [Fact]
    public async Task Add_IntoStrangersPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var ownerPortfolioId = await owner.CreatePortfolioAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.PostAsJsonAsync(DepositsUri(ownerPortfolioId), NewDepositRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var dbContext = CreateDbContext(ownerId);
        Assert.Equal(0, await dbContext.Set<TermDeposit>().IgnoreQueryFilters().CountAsync(cancellationToken));
        Assert.Equal(0, await dbContext.Assets.IgnoreQueryFilters().CountAsync(cancellationToken));
    }

    /// <summary>The terms row itself is tenant-filtered: another user's context never sees it.</summary>
    [Fact]
    public async Task TermDeposit_QueriedAsStranger_IsFilteredOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var (_, deposit) = await owner.CreatePortfolioWithDepositAsync(cancellationToken);

        await using var strangerDb = CreateDbContext(Guid.NewGuid());
        Assert.False(await strangerDb.Set<TermDeposit>().AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
        await using var ownerDb = CreateDbContext(ownerId);
        Assert.True(await ownerDb.Set<TermDeposit>().AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
    }
}
