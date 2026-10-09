using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class RestoreAssetEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Restore_ClearsFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.ArchiveAssetAsync(portfolioId, assetId, cancellationToken);

        var response = await client.PostAsync(RestoreAssetUri(portfolioId, assetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.False(body!.IsArchived);
        Assert.False((await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).IsArchived);
    }

    [Fact]
    public async Task Restore_NotArchived_IsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var before = await client.GetAssetAsync(portfolioId, assetId, cancellationToken);

        var response = await client.PostAsync(RestoreAssetUri(portfolioId, assetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken));
    }

    [Fact]
    public async Task Restore_ArchivedAsset_AcceptsTransactionsAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var cashId = await client.AddCashAssetWithBalanceAsync(portfolioId, cancellationToken, balance: 100m);
        await client.ArchiveAssetAsync(portfolioId, cashId, cancellationToken);
        await client.RestoreAssetAsync(portfolioId, cashId, cancellationToken);

        await client.RecordTransactionAsync(
            portfolioId, cashId, TransactionType.Deposit, 50m, new DateOnly(2026, 2, 1), cancellationToken, unitPrice: 1m);

        Assert.Equal(150m, (await client.GetAssetAsync(portfolioId, cashId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Restore_UnknownAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.PostAsync(RestoreAssetUri(portfolioId, Guid.NewGuid()), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Restore_ArchivedPortfolio_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.ArchiveAssetAsync(portfolioId, assetId, cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        var response = await client.PostAsync(RestoreAssetUri(portfolioId, assetId), content: null, cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        Assert.True((await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).IsArchived);
    }
}
