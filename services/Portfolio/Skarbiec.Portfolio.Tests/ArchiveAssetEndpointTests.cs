using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class ArchiveAssetEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Archive_SetsFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);

        var response = await client.PostAsync(ArchiveAssetUri(assetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(assetId, body!.Id);
        Assert.True(body.IsArchived);
        Assert.True((await client.GetAssetAsync(assetId, cancellationToken)).IsArchived);
    }

    [Fact]
    public async Task Archive_AlreadyArchived_IsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.ArchiveAssetAsync(assetId, cancellationToken);
        var before = await client.GetAssetAsync(assetId, cancellationToken);

        var response = await client.PostAsync(ArchiveAssetUri(assetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken));
    }

    [Fact]
    public async Task Archive_AssetWithPosition_KeepsQuantityAndTransactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 50m, new DateOnly(2026, 1, 1), cancellationToken);

        var response = await client.PostAsync(ArchiveAssetUri(assetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var asset = await client.GetAssetAsync(assetId, cancellationToken);
        Assert.True(asset.IsArchived);
        Assert.Equal(50m, asset.Quantity);
        Assert.Equal(1, (await client.ListTransactionsAsync(assetId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Archive_DueDeposit_ReturnsOkAndKeepsTerms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);

        var response = await client.PostAsync(ArchiveAssetUri(deposit.AssetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var archived = await client.GetDepositAsync(deposit.AssetId, cancellationToken);
        Assert.True(archived.IsArchived);
        Assert.Equal(DepositStatus.Due, archived.Status);
        Assert.Equal(10_000m, archived.Principal);
    }

    [Fact]
    public async Task Archive_UnknownAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsync(ArchiveAssetUri(Guid.NewGuid()), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Archive_ArchivedAccount_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.ArchiveAccountAsync(accountId, cancellationToken);

        var response = await client.PostAsync(ArchiveAssetUri(assetId), content: null, cancellationToken);

        await response.AssertAccountArchivedConflictAsync(cancellationToken);
        Assert.False((await client.GetAssetAsync(assetId, cancellationToken)).IsArchived);
    }
}
