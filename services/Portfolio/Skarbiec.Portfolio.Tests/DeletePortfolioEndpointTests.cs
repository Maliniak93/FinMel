using System.Net;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The DELETE under test is called directly and asserted on the raw response; PortfolioApi helpers only arrange.
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
    public async Task Delete_PortfolioWithSavingsAccount_DeletesTerms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken);
        var (_, kept) = await client.CreatePortfolioWithSavingsAccountAsync(
            cancellationToken, NewSavingsAccountRequest(name: "Kept"), portfolioName: "Other");

        var response = await client.DeleteAsync(PortfolioUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Assets.AnyAsync(a => a.Id == account.AssetId, cancellationToken));
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == account.AssetId, cancellationToken));
        Assert.False(await dbContext.Set<SavingsAccount>().IgnoreQueryFilters().AnyAsync(t => t.AssetId == account.AssetId, cancellationToken));
        Assert.True(await dbContext.Set<SavingsAccount>().AnyAsync(t => t.AssetId == kept.AssetId, cancellationToken));
    }

    [Fact]
    public async Task Delete_NonExistentPortfolio_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.DeleteAsync(PortfolioUri(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

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

    [Fact]
    public async Task Delete_PortfolioWithSavingsSettlements_DeletesSettlements()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        var (_, kept) = await client.CreatePortfolioWithSavingsAccountAsync(
            cancellationToken, NewInterestAccountRequest(name: "Kept"), portfolioName: "Other");
        await client.SettlePreviewedSavingsInterestAsync(portfolioId, account.AssetId, cancellationToken);
        await client.SettlePreviewedSavingsInterestAsync(kept.PortfolioId, kept.AssetId, cancellationToken);

        var response = await client.DeleteAsync(PortfolioUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await CountSavingsSettlementsAsync(userId, cancellationToken, account.AssetId));
        Assert.Equal(1, await CountSavingsSettlementsAsync(userId, cancellationToken, kept.AssetId));
    }

    [Fact]
    public async Task Delete_PortfolioWithSettledBond_DeletesSettlementsAndDetachesCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedBondAsync(cancellationToken, request: NewRorBondRequest());
        await client.SettleBondInterestAsync(
            funded.BondPortfolioId, funded.Bond.AssetId, [(1, null), (2, 3.75m)], funded.CashAssetId, cancellationToken);

        var response = await client.DeleteAsync(PortfolioUri(funded.BondPortfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Set<BondInterestSettlement>().IgnoreQueryFilters().AnyAsync(s => s.AssetId == funded.Bond.AssetId, cancellationToken));
        Assert.False(await dbContext.Assets.AnyAsync(a => a.Id == funded.Bond.AssetId, cancellationToken));
        Assert.Equal(1_025.91m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == funded.CashAssetId && t.TransferId != null, cancellationToken));
    }
}
