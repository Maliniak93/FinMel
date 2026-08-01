using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class DeleteTransactionEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Delete_FromFourTransactionHistory_RecomputesAssetCorrectly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 5m, new DateOnly(2026, 1, 2), cancellationToken);
        var sellId = await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Sell, 3m, new DateOnly(2026, 1, 3), cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Dividend, 0m, new DateOnly(2026, 1, 4), cancellationToken);
        // 10 + 5 - 3 = 12 before the delete.

        var response = await client.DeleteAsync(TransactionUri(portfolioId, assetId, sellId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(15m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        var page = await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(3, page.TotalCount);
        Assert.DoesNotContain(page.Items, t => t.Id == sellId);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, assetId, cancellationToken);
    }

    [Fact]
    public async Task Delete_ThatBreaksLaterSell_ReturnsConflictAndNothingChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Sell, 8m, new DateOnly(2026, 1, 2), cancellationToken);
        // Removing the Buy would leave a lone Sell 8 with nothing to sell from.

        var response = await client.DeleteAsync(TransactionUri(portfolioId, assetId, buyId), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        var page = await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(2, page.TotalCount);
        Assert.Contains(page.Items, t => t.Id == buyId);
    }

    [Fact]
    public async Task Delete_LastTransaction_UnblocksAssetRemoval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        // RemoveAsset (T1.2) 409s while TransactionCount > 0 — proves DeleteTransaction decrements it.
        var blockedRemoval = await client.DeleteAsync(AssetUri(portfolioId, assetId), cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, blockedRemoval.StatusCode);

        await client.DeleteAsync(TransactionUri(portfolioId, assetId, buyId), cancellationToken);
        var removal = await client.DeleteAsync(AssetUri(portfolioId, assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
    }

    [Fact]
    public async Task Delete_ForNonExistentTransaction_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);

        var response = await client.DeleteAsync(TransactionUri(portfolioId, assetId, Guid.NewGuid()), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AssetUnderWrongPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        var otherPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Other portfolio");

        var response = await client.DeleteAsync(TransactionUri(otherPortfolioId, assetId, buyId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.DeleteAsync(
            TransactionUri(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
