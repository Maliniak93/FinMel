using System.Net;
using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task Remove_LastAsset_UnblocksPortfolioDelete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);

        await client.DeleteAsync(AssetUri(portfolioId, assetId), cancellationToken);
        var deletePortfolio = await client.DeleteAsync(PortfolioUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deletePortfolio.StatusCode);
    }

    [Fact]
    public async Task Remove_AssetWithTransactions_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(ownerId);
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);

        // Transaction doesn't exist as an entity until T1.3 (which depends on this task) —
        // TransactionCount is the denormalized stand-in T1.3's RecordTransaction will maintain.
        // Seeded directly here since there's no API to record a real transaction yet.
        await BumpTransactionCountAsync(ownerId, assetId, cancellationToken);

        var response = await client.DeleteAsync(AssetUri(portfolioId, assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var getAfterFailedDelete = await client.GetAsync(AssetUri(portfolioId, assetId), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getAfterFailedDelete.StatusCode);
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

    private async Task BumpTransactionCountAsync(Guid ownerId, Guid assetId, CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext(ownerId);
        var asset = await dbContext.Assets.SingleAsync(a => a.Id == assetId, cancellationToken);
        asset.TransactionCount = 1;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
