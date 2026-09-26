using System.Net;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class RemoveAssetEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Remove_AssetWithNoTransactions_ReturnsNoContentAndRemovesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);

        var response = await client.DeleteAsync(AssetUri(portfolioId, assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getAfterDelete = await client.GetAsync(AssetUri(portfolioId, assetId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    /// <summary>spec-08 AC-3: removing an asset cascades to its transactions — no "delete them first" step.</summary>
    [Fact]
    public async Task Remove_AssetWithTransactions_RemovesAssetAndItsTransactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 2m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Sell, 1m, new DateOnly(2026, 1, 2), cancellationToken);

        var response = await client.DeleteAsync(AssetUri(portfolioId, assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getAfterDelete = await client.GetAsync(AssetUri(portfolioId, assetId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);

        // No endpoint lists a deleted asset's transactions, so the orphan check reads the database.
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == assetId, cancellationToken));
    }

    [Fact]
    public async Task Remove_NonExistentAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.DeleteAsync(AssetUri(portfolioId, Guid.NewGuid()), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>archived-portfolio-out-of-net-worth AC6: removing an asset of an archived portfolio
    /// is a 409 <c>Conflict.PortfolioArchived</c>; the asset and its transactions stay.</summary>
    [Fact]
    public async Task RemoveAsset_InArchivedPortfolio_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 2m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        var response = await client.DeleteAsync(AssetUri(portfolioId, assetId), cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        var stillThere = await client.GetAssetAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(2m, stillThere.Quantity);
        Assert.Equal(1, (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).TotalCount);
    }

    /// <summary>
    /// term-deposits AC-11 (HTTP half): removing a term deposit through the ordinary asset endpoint
    /// takes the asset, its opening transaction and its <c>TermDeposit</c> row with it. The
    /// <c>AssetRemoved</c> event written in the same save is proven hostlessly by
    /// <see cref="PortfolioOutboxTests.RemoveAsset_TermDeposit_WritesAssetRemovedAndDeletesTermsInOneSave"/> —
    /// this host runs a live bus whose delivery poller would race an outbox read.
    /// </summary>
    [Fact]
    public async Task Remove_TermDeposit_DeletesTermsAndPublishesAssetRemoved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var (_, kept) = await client.CreatePortfolioWithDepositAsync(cancellationToken, NewDepositRequest(name: "Kept"), portfolioName: "Other");

        var response = await client.DeleteAsync(AssetUri(portfolioId, deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(AssetUri(portfolioId, deposit.AssetId), cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(DepositUri(portfolioId, deposit.AssetId), cancellationToken)).StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Assets.AnyAsync(a => a.Id == deposit.AssetId, cancellationToken));
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
        Assert.False(await dbContext.Set<TermDeposit>().IgnoreQueryFilters().AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
        Assert.True(await dbContext.Set<TermDeposit>().AnyAsync(t => t.AssetId == kept.AssetId, cancellationToken));
    }

    /// <summary>
    /// asset-transfers-deposit-funding AC-7: removing a funded deposit detaches — never reverses —
    /// its transfer. The Cash Withdraw stays with <c>TransferId</c> null and Cash stays at 4 000; the
    /// leg is now an ordinary transaction, so deleting it afterwards returns Cash to 5 000.
    /// </summary>
    [Fact]
    public async Task Remove_FundedDeposit_DetachesCashLeg()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedDepositAsync(cancellationToken);
        var leg = await client.GetCashWithdrawAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);

        var response = await client.DeleteAsync(AssetUri(funded.DepositPortfolioId, funded.Deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(AssetUri(funded.DepositPortfolioId, funded.Deposit.AssetId), cancellationToken)).StatusCode);
        Assert.Equal(4_000m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
        var detached = await client.GetCashWithdrawAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);
        Assert.Equal(leg.Id, detached.Id);
        Assert.Equal(1_000m, detached.Quantity);
        Assert.Null(detached.Transfer);
        await using (var dbContext = CreateDbContext(userId))
        {
            var stored = await dbContext.Transactions.SingleAsync(t => t.Id == leg.Id, cancellationToken);
            Assert.Null(stored.TransferId);
            Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
        }

        var deleteLeg = await client.DeleteAsync(TransactionUri(funded.CashPortfolioId, funded.CashAssetId, leg.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleteLeg.StatusCode);
        Assert.Equal(5_000m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
    }

    /// <summary>
    /// asset-transfers-deposit-funding AC-7: removing the funding Cash asset instead leaves the
    /// deposit and its opening transaction in place, unlinked, at the same quantity — and the deposit
    /// no longer reports a funding asset.
    /// </summary>
    [Fact]
    public async Task Remove_FundingCash_DetachesDepositLeg()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedDepositAsync(cancellationToken);
        var opening = Assert.Single((await client.ListTransactionsAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken)).Items);

        var response = await client.DeleteAsync(AssetUri(funded.CashPortfolioId, funded.CashAssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(AssetUri(funded.CashPortfolioId, funded.CashAssetId), cancellationToken)).StatusCode);
        var deposit = await client.GetDepositAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken);
        Assert.Equal(1_000m, deposit.Principal);
        Assert.Null(deposit.FundingAssetId);
        Assert.Null(deposit.FundingAssetName);
        Assert.Equal(1_000m, (await client.GetAssetAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken)).Quantity);
        var kept = Assert.Single((await client.ListTransactionsAsync(funded.DepositPortfolioId, funded.Deposit.AssetId, cancellationToken)).Items);
        Assert.Equal(opening.Id, kept.Id);
        Assert.Equal(1_000m, kept.Quantity);
        Assert.Null(kept.Transfer);
        await using var dbContext = CreateDbContext(userId);
        Assert.Null((await dbContext.Transactions.SingleAsync(t => t.Id == opening.Id, cancellationToken)).TransferId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == funded.CashAssetId, cancellationToken));
    }

    /// <summary>
    /// term-deposits-settlement AC-7 (delete half): settling does not lock the deposit against
    /// deletion — the asset, both its transactions (opening + net-interest credit) and its terms go.
    /// </summary>
    [Fact]
    public async Task Remove_SettledDeposit_RemovesItAsInPartOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        await client.SettleDepositAsync(portfolioId, deposit.AssetId, cancellationToken);

        var response = await client.DeleteAsync(AssetUri(portfolioId, deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(DepositUri(portfolioId, deposit.AssetId), cancellationToken)).StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Assets.AnyAsync(a => a.Id == deposit.AssetId, cancellationToken));
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
        Assert.False(await dbContext.Set<TermDeposit>().IgnoreQueryFilters().AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
    }
}
