using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class TransactionTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Put_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (ownerPortfolioId, ownerAssetId, transactionId) = await CreateOwnedTransactionAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.PutAsJsonAsync(
            TransactionUri(ownerAssetId, transactionId), UpdatePayload(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (ownerPortfolioId, ownerAssetId, transactionId) = await CreateOwnedTransactionAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.DeleteAsync(TransactionUri(ownerAssetId, transactionId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_ForStrangersOwnAsset_DoesNotIncludeResource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateOwnedTransactionAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (strangerPortfolioId, strangerAssetId) = await stranger.CreatePortfolioWithAssetAsync(cancellationToken);

        var page = await stranger.ListTransactionsAsync(strangerAssetId, cancellationToken);

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Put_TransactionUnderStrangersOwnPortfolioAndAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, _, transactionId) = await CreateOwnedTransactionAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (strangerPortfolioId, strangerAssetId) = await stranger.CreatePortfolioWithAssetAsync(cancellationToken);

        var response = await stranger.PutAsJsonAsync(
            TransactionUri(strangerAssetId, transactionId), UpdatePayload(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_TransactionUnderStrangersOwnPortfolioAndAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, _, transactionId) = await CreateOwnedTransactionAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (strangerPortfolioId, strangerAssetId) = await stranger.CreatePortfolioWithAssetAsync(cancellationToken);

        var response = await stranger.DeleteAsync(TransactionUri(strangerAssetId, transactionId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(Guid PortfolioId, Guid AssetId, Guid TransactionId)> CreateOwnedTransactionAsync(CancellationToken cancellationToken)
    {
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await owner.CreatePortfolioWithAssetAsync(cancellationToken);
        var transactionId = await owner.RecordTransactionAsync(
            assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);

        return (portfolioId, assetId, transactionId);
    }

    private static UpdateTransactionRequest UpdatePayload() => new()
    {
        Type = TransactionType.Buy,
        Quantity = 1m,
        UnitPrice = 1m,
        Date = new DateOnly(2026, 1, 1)
    };
}
