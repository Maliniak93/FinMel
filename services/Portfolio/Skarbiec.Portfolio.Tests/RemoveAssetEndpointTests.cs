using System.Net;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
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
}
