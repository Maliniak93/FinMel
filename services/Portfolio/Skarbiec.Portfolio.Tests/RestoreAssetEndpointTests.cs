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
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.ArchiveAssetAsync(assetId, cancellationToken);

        var response = await client.PostAsync(RestoreAssetUri(assetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.False(body!.IsArchived);
        Assert.False((await client.GetAssetAsync(assetId, cancellationToken)).IsArchived);
    }

    [Fact]
    public async Task Restore_NotArchived_IsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var before = await client.GetAssetAsync(assetId, cancellationToken);

        var response = await client.PostAsync(RestoreAssetUri(assetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken));
    }

    [Fact]
    public async Task Restore_ArchivedAsset_AcceptsTransactionsAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 100m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.ArchiveAssetAsync(assetId, cancellationToken);
        await client.RestoreAssetAsync(assetId, cancellationToken);

        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 50m, new DateOnly(2026, 2, 1), cancellationToken);

        Assert.Equal(150m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Restore_UnknownAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsync(RestoreAssetUri(Guid.NewGuid()), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Restore_ArchivedAccount_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.ArchiveAssetAsync(assetId, cancellationToken);
        await client.ArchiveAccountAsync(accountId, cancellationToken);

        var response = await client.PostAsync(RestoreAssetUri(assetId), content: null, cancellationToken);

        await response.AssertAccountArchivedConflictAsync(cancellationToken);
        Assert.True((await client.GetAssetAsync(assetId, cancellationToken)).IsArchived);
    }
}
