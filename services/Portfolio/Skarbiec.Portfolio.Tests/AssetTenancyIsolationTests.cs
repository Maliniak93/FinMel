using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class AssetTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Get_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerAccountId, assetId) = await owner.CreateAccountWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.GetAsync(AssetUri(assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerAccountId, assetId) = await owner.CreateAccountWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.PutAsJsonAsync(AssetUri(assetId), UpdatePayload(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ByStranger_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerAccountId, assetId) = await owner.CreateAccountWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.DeleteAsync(AssetUri(assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_ForStrangersOwnAccount_DoesNotIncludeResource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await owner.CreateAccountWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerAccountId = await stranger.CreateAccountAsync(cancellationToken);

        var response = await stranger.GetAsync(AssetsUri(strangerAccountId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<AssetResponse>>(cancellationToken);
        Assert.Empty(assets!);
    }

    [Fact]
    public async Task Get_AssetUnderStrangersOwnAccount_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await owner.CreateAccountWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.GetAsync(AssetUri(assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_AssetUnderStrangersOwnAccount_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await owner.CreateAccountWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.PutAsJsonAsync(AssetUri(assetId), UpdatePayload(), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AssetUnderStrangersOwnAccount_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await owner.CreateAccountWithAssetAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.DeleteAsync(AssetUri(assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AssetWithTransactionsByStranger_ReturnsNotFoundAndLeavesTransactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (ownerAccountId, assetIds) = await owner.CreateAccountWithAssetsAndTransactionsAsync(
            cancellationToken, assetCount: 1, transactionsPerAsset: 2);
        var assetId = assetIds[0];

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await stranger.DeleteAsync(AssetUri(assetId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var getAsset = await owner.GetAsync(AssetUri(assetId), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getAsset.StatusCode);
        Assert.Equal(2, (await owner.ListTransactionsAsync(assetId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Get_KpiFields_NeverExposeOtherUsersTransactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, ownerAssetId) = await owner.CreateAccountWithAssetAsync(cancellationToken);
        await owner.RecordTransactionAsync(ownerAssetId, TransactionType.Buy, 5m, new DateOnly(2026, 1, 5), cancellationToken, unitPrice: 10m);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, strangerAssetId) = await stranger.CreateAccountWithAssetAsync(cancellationToken);

        var foreign = await stranger.GetAsync(AssetUri(ownerAssetId), cancellationToken);
        var own = await stranger.GetAsync(AssetUri(strangerAssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        var body = (await own.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!;
        Assert.Multiple(
            () => Assert.Equal(0m, body.UnitCount),
            () => Assert.Equal(0m, body.CostPln ?? 0m),
            () => Assert.Equal(0m, body.DividendPln ?? 0m));
    }

    private static UpdateAssetRequest UpdatePayload() => new()
    {
        AssetClass = AssetClass.Cash,
        Name = "Stranger's edit",
        Currency = "PLN"
    };
}
