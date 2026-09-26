using System.Net;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The DELETE under test is called directly and asserted on the raw response; PortfolioApi helpers
// only arrange.
[Collection(TestingDefaults.CollectionName)]
public sealed class DeletePortfolioEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Delete_PortfolioWithNoAssets_ReturnsNoContentAndRemovesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.DeleteAsync(PortfolioUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getAfterDelete = await client.GetAsync(PortfolioUri(portfolioId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    /// <summary>spec-08 AC-1: the delete cascades to the assets and their transactions — no "empty it first" step.</summary>
    [Fact]
    public async Task Delete_PortfolioWithAssetsAndTransactions_RemovesEverything()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetIds) = await client.CreatePortfolioWithAssetsAndTransactionsAsync(
            cancellationToken, assetCount: 2, transactionsPerAsset: 2);

        var response = await client.DeleteAsync(PortfolioUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var getPortfolio = await client.GetAsync(PortfolioUri(portfolioId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getPortfolio.StatusCode);

        foreach (var assetId in assetIds)
        {
            var getAsset = await client.GetAsync(AssetUri(portfolioId, assetId), cancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, getAsset.StatusCode);
        }
    }

    /// <summary>
    /// asset-transfers-deposit-funding AC-7: deleting the portfolio that holds the funding Cash
    /// detaches the deposit's leg in the other portfolio — the deposit, its opening transaction and
    /// its quantity stay, unlinked.
    /// </summary>
    [Fact]
    public async Task Delete_PortfolioWithTransferCounterpartOutside_DetachesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedDepositAsync(cancellationToken);
        var opening = Assert.Single((await client.ListTransactionsAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken)).Items);

        var response = await client.DeleteAsync(PortfolioUri(funded.CashPortfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(PortfolioUri(funded.CashPortfolioId), cancellationToken)).StatusCode);
        var deposit = await client.GetDepositAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken);
        Assert.Null(deposit.FundingAssetId);
        Assert.Null(deposit.FundingAssetName);
        Assert.Equal(1_000m, (await client.GetAssetAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken)).Quantity);
        var kept = Assert.Single((await client.ListTransactionsAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken)).Items);
        Assert.Equal(opening.Id, kept.Id);
        Assert.Null(kept.Transfer);
        await using var dbContext = CreateDbContext(userId);
        Assert.Null((await dbContext.Transactions.SingleAsync(t => t.Id == opening.Id, cancellationToken)).TransferId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
    }

    /// <summary>
    /// asset-transfers-deposit-funding: a transfer whose both legs sit in the deleted portfolio has no
    /// surviving counterpart — both legs go with it and nothing else is touched.
    /// </summary>
    [Fact]
    public async Task Delete_PortfolioHoldingBothTransferLegs_RemovesThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Everything");
        var cashId = await client.AddCashAssetWithBalanceAsync(portfolioId, cancellationToken);
        var deposit = await client.AddDepositAsync(
            portfolioId, cancellationToken, NewDepositRequest(principal: 1_000m, fundingAssetId: cashId));

        var response = await client.DeleteAsync(PortfolioUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Transactions.AnyAsync(cancellationToken));
        Assert.False(await dbContext.Assets.AnyAsync(a => a.Id == deposit.AssetId || a.Id == cashId, cancellationToken));
    }

    [Fact]
    public async Task Delete_NonExistentPortfolio_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.DeleteAsync(PortfolioUri(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>archived-portfolio-out-of-net-worth AC8: the read-only rule does not block deleting
    /// an archived portfolio — the delete still cascades to its assets and succeeds.</summary>
    [Fact]
    public async Task Delete_ArchivedPortfolio_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetIds) = await client.CreatePortfolioWithAssetsAndTransactionsAsync(
            cancellationToken, assetCount: 1, transactionsPerAsset: 1);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        var response = await client.DeleteAsync(PortfolioUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getPortfolio = await client.GetAsync(PortfolioUri(portfolioId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getPortfolio.StatusCode);
        var getAsset = await client.GetAsync(AssetUri(portfolioId, assetIds[0]), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAsset.StatusCode);
    }
}
