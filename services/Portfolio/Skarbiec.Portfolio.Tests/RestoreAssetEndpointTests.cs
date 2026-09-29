using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

/// <summary>asset-archive: <c>POST .../assets/{id}/restore</c>. The event half is <see cref="PortfolioOutboxTests"/>.</summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class RestoreAssetEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    /// <summary>AC-2: 200 with <c>isArchived</c> false, and the flag is cleared.</summary>
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

    /// <summary>AC-2: restoring an asset that is not archived is 200 with the unchanged body.</summary>
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

    /// <summary>After a restore the asset is writable again.</summary>
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

    /// <summary>AC-3: an unknown id is a 404.</summary>
    [Fact]
    public async Task Restore_UnknownAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.PostAsync(RestoreAssetUri(portfolioId, Guid.NewGuid()), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>AC-3 + design: restoring an asset inside an archived portfolio is a 409 <c>Conflict.PortfolioArchived</c>; the flag stays.</summary>
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
