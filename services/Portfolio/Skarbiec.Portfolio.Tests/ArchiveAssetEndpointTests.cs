using System.Net;
using System.Net.Http.Json;
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
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);

        var response = await client.PostAsync(ArchiveAssetUri(portfolioId, assetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(assetId, body!.Id);
        Assert.True(body.IsArchived);
        Assert.True((await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).IsArchived);
    }

    [Fact]
    public async Task Archive_AlreadyArchived_IsIdempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.ArchiveAssetAsync(portfolioId, assetId, cancellationToken);
        var before = await client.GetAssetAsync(portfolioId, assetId, cancellationToken);

        var response = await client.PostAsync(ArchiveAssetUri(portfolioId, assetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken));
    }

    [Fact]
    public async Task Archive_CashWithBalance_KeepsQuantityAndTransactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var cashId = await client.AddCashAssetWithBalanceAsync(portfolioId, cancellationToken, balance: 5_000m);

        var response = await client.PostAsync(ArchiveAssetUri(portfolioId, cashId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var asset = await client.GetAssetAsync(portfolioId, cashId, cancellationToken);
        Assert.True(asset.IsArchived);
        Assert.Equal(5_000m, asset.Quantity);
        Assert.Equal(1, (await client.ListTransactionsAsync(portfolioId, cashId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Archive_DueDeposit_ReturnsOkAndKeepsTerms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);

        var response = await client.PostAsync(ArchiveAssetUri(portfolioId, deposit.AssetId), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var archived = await client.GetDepositAsync(portfolioId, deposit.AssetId, cancellationToken);
        Assert.True(archived.IsArchived);
        Assert.Equal(DepositStatus.Due, archived.Status);
        Assert.Equal(10_000m, archived.Principal);
    }

    [Fact]
    public async Task Archive_UnknownAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.PostAsync(ArchiveAssetUri(portfolioId, Guid.NewGuid()), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Archive_ArchivedPortfolio_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        var response = await client.PostAsync(ArchiveAssetUri(portfolioId, assetId), content: null, cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        Assert.False((await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).IsArchived);
    }
}
