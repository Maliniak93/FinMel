using System.Net;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class RemoveAssetEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Remove_AssetWithNoTransactions_ReturnsNoContentAndRemovesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);

        var response = await client.DeleteAsync(AssetUri(portfolioId, assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getAfterDelete = await client.GetAsync(AssetUri(portfolioId, assetId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    /// <summary>spec-08 AC-3: removing an asset cascades to its transactions — no "delete them first" step.</summary>
    [Fact]
    public async Task Remove_AssetWithTransactions_RemovesAssetAndItsTransactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 2m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Sell, 1m, new DateOnly(2026, 1, 2), cancellationToken);

        var response = await client.DeleteAsync(AssetUri(portfolioId, assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getAfterDelete = await client.GetAsync(AssetUri(portfolioId, assetId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);

        // No endpoint lists a deleted asset's transactions, so the orphan check reads the database.
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == assetId, cancellationToken));
    }

    [Fact]
    public async Task Remove_NonExistentAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.DeleteAsync(AssetUri(portfolioId, Guid.NewGuid()), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>archived-portfolio-out-of-net-worth AC6: removing an asset of an archived portfolio
    /// is a 409 <c>Conflict.PortfolioArchived</c>; the asset and its transactions stay.</summary>
    [Fact]
    public async Task RemoveAsset_InArchivedPortfolio_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 2m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        var response = await client.DeleteAsync(AssetUri(portfolioId, assetId), cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        var stillThere = await client.GetAssetAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(2m, stillThere.Quantity);
        Assert.Equal(1, (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).TotalCount);
    }

    /// <summary>
    /// term-deposits AC-11 (HTTP half): removing a term deposit through the ordinary asset endpoint
    /// takes the asset, its opening transaction and its <c>TermDeposit</c> row with it. The
    /// <c>AssetRemoved</c> event written in the same save is proven hostlessly by
    /// <see cref="PortfolioOutboxTests.RemoveAsset_TermDeposit_WritesAssetRemovedAndDeletesTermsInOneSave"/> —
    /// this host runs a live bus whose delivery poller would race an outbox read.
    /// </summary>
    [Fact]
    public async Task Remove_TermDeposit_DeletesTermsAndPublishesAssetRemoved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var (_, kept) = await client.CreatePortfolioWithDepositAsync(cancellationToken, NewDepositRequest(name: "Kept"), portfolioName: "Other");

        var response = await client.DeleteAsync(AssetUri(portfolioId, deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(AssetUri(portfolioId, deposit.AssetId), cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(DepositUri(portfolioId, deposit.AssetId), cancellationToken)).StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Assets.AnyAsync(a => a.Id == deposit.AssetId, cancellationToken));
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
        Assert.False(await dbContext.Set<TermDeposit>().IgnoreQueryFilters().AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
        Assert.True(await dbContext.Set<TermDeposit>().AnyAsync(t => t.AssetId == kept.AssetId, cancellationToken));
    }

    /// <summary>
    /// term-deposits-settlement AC-7 (delete half): settling does not lock the deposit against
    /// deletion — the asset, both its transactions (opening + net-interest credit) and its terms go.
    /// </summary>
    [Fact]
    public async Task Remove_SettledDeposit_RemovesItAsInPartOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.SettleDepositAsync(portfolioId, deposit.AssetId, cancellationToken);

        var response = await client.DeleteAsync(AssetUri(portfolioId, deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(DepositUri(portfolioId, deposit.AssetId), cancellationToken)).StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Assets.AnyAsync(a => a.Id == deposit.AssetId, cancellationToken));
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
        Assert.False(await dbContext.Set<TermDeposit>().IgnoreQueryFilters().AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
    }
}
