using System.Net;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
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

    /// <summary>asset-archive AC-4: removing an archived asset stays allowed — it and its transactions are gone (the <c>AssetRemoved</c> event is in the outbox tests).</summary>
    [Fact]
    public async Task RemoveAsset_ArchivedAsset_Deletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var cashId = await client.AddCashAssetWithBalanceAsync(portfolioId, cancellationToken, balance: 100m);
        await client.ArchiveAssetAsync(portfolioId, cashId, cancellationToken);

        var response = await client.DeleteAsync(AssetUri(portfolioId, cashId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(AssetUri(portfolioId, cashId), cancellationToken)).StatusCode);
        var rows = await SnapshotUserRowsAsync(userId, cancellationToken);
        Assert.Equal(0, rows.Assets);
        Assert.Equal(0, rows.Transactions);
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
    /// savings-accounts AC-8: removing a savings account through the ordinary asset endpoint takes the
    /// asset, its transactions and its <c>SavingsAccount</c> row with it; another account stays.
    /// </summary>
    [Fact]
    public async Task Remove_SavingsAccount_DeletesTerms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken);
        await client.RecordTransactionAsync(
            portfolioId, account.AssetId, TransactionType.Withdraw, 100m, SavingsToday, cancellationToken, unitPrice: 1m);
        var kept = await client.AddSavingsAccountAsync(portfolioId, cancellationToken, NewSavingsAccountRequest(name: "Kept"));

        var response = await client.DeleteAsync(AssetUri(portfolioId, account.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(AssetUri(portfolioId, account.AssetId), cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(SavingsAccountUri(portfolioId, account.AssetId), cancellationToken)).StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Assets.AnyAsync(a => a.Id == account.AssetId, cancellationToken));
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == account.AssetId, cancellationToken));
        Assert.False(await dbContext.Set<SavingsAccount>().IgnoreQueryFilters().AnyAsync(t => t.AssetId == account.AssetId, cancellationToken));
        Assert.True(await dbContext.Set<SavingsAccount>().AnyAsync(t => t.AssetId == kept.AssetId, cancellationToken));
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

    /// <summary>
    /// deposit-payout-to-cash AC-5 (remove half): removing a paid-out deposit detaches — never
    /// reverses — its payout. The Cash keeps its 10 119.83 through the leg, now an ordinary Deposit
    /// with no transfer link.
    /// </summary>
    [Fact]
    public async Task Remove_PaidOutDeposit_KeepsCashBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var paidOut = await client.CreatePaidOutDepositAsync(cancellationToken);
        var leg = Assert.Single((await client.ListTransactionsAsync(paidOut.CashPortfolioId, paidOut.CashAssetId, cancellationToken)).Items);

        var response = await client.DeleteAsync(AssetUri(paidOut.DepositPortfolioId, paidOut.Deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync(DepositUri(paidOut.DepositPortfolioId, paidOut.Deposit.AssetId), cancellationToken)).StatusCode);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(paidOut.CashPortfolioId, paidOut.CashAssetId, cancellationToken)).Quantity);
        var kept = Assert.Single((await client.ListTransactionsAsync(paidOut.CashPortfolioId, paidOut.CashAssetId, cancellationToken)).Items);
        Assert.Equal(leg.Id, kept.Id);
        Assert.Equal(TransactionType.Deposit, kept.Type);
        Assert.Equal(10_119.83m, kept.Quantity);
        Assert.Null(kept.Transfer);
        await using var dbContext = CreateDbContext(userId);
        Assert.Null((await dbContext.Transactions.SingleAsync(t => t.Id == leg.Id, cancellationToken)).TransferId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == paidOut.Deposit.AssetId, cancellationToken));
    }

    /// <summary>
    /// savings-interest-settlement AC-10: removing a savings account deletes its interest settlements
    /// with it, while another account's settlement stays.
    /// </summary>
    [Fact]
    public async Task Remove_SavingsAccount_DeletesSettlements()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        var kept = await client.AddSavingsAccountAsync(portfolioId, cancellationToken, NewInterestAccountRequest(name: "Kept"));
        await client.SettlePreviewedSavingsInterestAsync(portfolioId, account.AssetId, cancellationToken);
        await client.SettlePreviewedSavingsInterestAsync(portfolioId, kept.AssetId, cancellationToken);

        var response = await client.DeleteAsync(AssetUri(portfolioId, account.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await CountSavingsSettlementsAsync(userId, cancellationToken, account.AssetId));
        Assert.Equal(1, await CountSavingsSettlementsAsync(userId, cancellationToken, kept.AssetId));
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Set<SavingsInterestSettlement>().IgnoreQueryFilters().AnyAsync(s => s.AssetId == account.AssetId, cancellationToken));
    }

    /// <summary>
    /// deposit-payout-to-savings AC-6: removing the savings account a deposit was paid into detaches the
    /// payout - the deposit stays PaidOut with a null destination name and holds 0.
    /// </summary>
    [Fact]
    public async Task Remove_SavingsAccountPaidFromDeposit_KeepsDepositPaidOut()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateDepositPaidIntoSavingsAsync(cancellationToken);

        var response = await client.DeleteAsync(AssetUri(setup.SavingsPortfolioId, setup.Account.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var deposit = await client.GetDepositAsync(setup.DepositPortfolioId, setup.Deposit.AssetId, cancellationToken);
        Assert.Equal(DepositStatus.PaidOut, deposit.Status);
        Assert.Null(deposit.PaidOutToAssetName);
        Assert.Equal(0m, (await client.GetAssetAsync(setup.DepositPortfolioId, setup.Deposit.AssetId, cancellationToken)).Quantity);
        var withdraw = Assert.Single(
            (await client.ListTransactionsAsync(setup.DepositPortfolioId, setup.Deposit.AssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);
        Assert.Null(withdraw.Transfer);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
    }

    /// <summary>
    /// deposit-payout-to-savings AC-6: removing the deposit detaches the payout - the savings account keeps
    /// its balance through the leg, now an ordinary Deposit with no transfer link.
    /// </summary>
    [Fact]
    public async Task Remove_DepositPaidIntoSavings_KeepsSavingsBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateDepositPaidIntoSavingsAsync(cancellationToken);
        var leg = Assert.Single((await client.ListTransactionsAsync(setup.SavingsPortfolioId, setup.Account.AssetId, cancellationToken)).Items);

        var response = await client.DeleteAsync(AssetUri(setup.DepositPortfolioId, setup.Deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(setup.SavingsPortfolioId, setup.Account.AssetId, cancellationToken)).Quantity);
        var kept = Assert.Single((await client.ListTransactionsAsync(setup.SavingsPortfolioId, setup.Account.AssetId, cancellationToken)).Items);
        Assert.Equal(leg.Id, kept.Id);
        Assert.Equal(TransactionType.Deposit, kept.Type);
        Assert.Equal(10_119.83m, kept.Quantity);
        Assert.Null(kept.Transfer);
        await using var dbContext = CreateDbContext(userId);
        Assert.Null((await dbContext.Transactions.SingleAsync(t => t.Id == leg.Id, cancellationToken)).TransferId);
    }
}
