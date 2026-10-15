using System.Net;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class RemoveAssetEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Remove_AssetWithNoTransactions_ReturnsNoContentAndRemovesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);

        var response = await client.DeleteAsync(AssetUri(assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getAfterDelete = await client.GetAsync(AssetUri(assetId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task Remove_AssetWithTransactions_RemovesAssetAndItsTransactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 2m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Sell, 1m, new DateOnly(2026, 1, 2), cancellationToken);

        var response = await client.DeleteAsync(AssetUri(assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getAfterDelete = await client.GetAsync(AssetUri(assetId), cancellationToken);
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

        var response = await client.DeleteAsync(AssetUri(Guid.NewGuid()), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveAsset_ArchivedAsset_Deletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (_, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 100m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.ArchiveAssetAsync(assetId, cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.DeleteAsync(AssetUri(assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(AssetUri(assetId), cancellationToken)).StatusCode);
        var rows = await SnapshotUserRowsAsync(userId, cancellationToken);
        Assert.Equal(before.Assets - 1, rows.Assets);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(TransactionsUri(assetId), cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task RemoveAsset_InArchivedAccount_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 2m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Sell, 2m, new DateOnly(2026, 1, 2), cancellationToken);
        await client.ArchiveAccountAsync(accountId, cancellationToken);

        var response = await client.DeleteAsync(AssetUri(assetId), cancellationToken);

        await response.AssertAccountArchivedConflictAsync(cancellationToken);
        var stillThere = await client.GetAssetAsync(assetId, cancellationToken);
        Assert.Equal(0m, stillThere.Quantity);
        Assert.Equal(2, (await client.ListTransactionsAsync(assetId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Remove_TermDeposit_DeletesTermsAndPublishesAssetRemoved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var deposit = await client.AddDepositAsync(cancellationToken);
        var kept = await client.AddDepositAsync(cancellationToken, NewDepositRequest(name: "Kept"));

        var response = await client.DeleteAsync(AssetUri(deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(AssetUri(deposit.AssetId), cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(DepositUri(deposit.AssetId), cancellationToken)).StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Assets.AnyAsync(a => a.Id == deposit.AssetId, cancellationToken));
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
        Assert.False(await dbContext.Set<TermDeposit>().IgnoreQueryFilters().AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
        Assert.True(await dbContext.Set<TermDeposit>().AnyAsync(t => t.AssetId == kept.AssetId, cancellationToken));
    }

    [Fact]
    public async Task Remove_BankAccountMoney_ReturnsAccountCashManaged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateSavingsBankAccountAsync(cancellationToken);

        var response = await client.DeleteAsync(AssetUri(account.MoneyAssetId), cancellationToken);

        await response.AssertAccountCashManagedAsync(cancellationToken);
        Assert.Equal(10_000m, (await client.GetAssetAsync(account.MoneyAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Remove_FundedDeposit_DetachesCashLeg()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedDepositAsync(cancellationToken);
        var leg = await client.GetCashWithdrawAsync(funded.CashAssetId, cancellationToken);

        var response = await client.DeleteAsync(AssetUri(funded.Deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(AssetUri(funded.Deposit.AssetId), cancellationToken)).StatusCode);
        Assert.Equal(4_000m, (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
        var detached = await client.GetCashWithdrawAsync(funded.CashAssetId, cancellationToken);
        Assert.Equal(leg.Id, detached.Id);
        Assert.Equal(1_000m, detached.Quantity);
        Assert.Null(detached.Transfer);
        await using (var dbContext = CreateDbContext(userId))
        {
            var stored = await dbContext.Transactions.SingleAsync(t => t.Id == leg.Id, cancellationToken);
            Assert.Null(stored.TransferId);
            Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
        }

        var deleteLeg = await client.DeleteAsync(TransactionUri(funded.CashAssetId, leg.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleteLeg.StatusCode);
        Assert.Equal(5_000m, (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Remove_Bond_DeletesTermsAndDetachesFunding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedBondAsync(cancellationToken);
        var leg = await client.GetCashWithdrawAsync(funded.CashAssetId, cancellationToken);

        var response = await client.DeleteAsync(AssetUri(funded.Bond.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(BondUri(funded.Bond.AssetId), cancellationToken)).StatusCode);
        Assert.Equal(1_000m, (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
        var detached = await client.GetCashWithdrawAsync(funded.CashAssetId, cancellationToken);
        Assert.Equal(leg.Id, detached.Id);
        Assert.Null(detached.Transfer);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Set<TreasuryBond>().IgnoreQueryFilters().AnyAsync(t => t.AssetId == funded.Bond.AssetId, cancellationToken));
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
    }

    [Fact]
    public async Task Remove_SettledDeposit_RemovesItAsInPartOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var deposit = await client.AddDepositAsync(cancellationToken);
        await client.SettleDepositAsync(deposit.AssetId, cancellationToken);

        var response = await client.DeleteAsync(AssetUri(deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(DepositUri(deposit.AssetId), cancellationToken)).StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Assets.AnyAsync(a => a.Id == deposit.AssetId, cancellationToken));
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
        Assert.False(await dbContext.Set<TermDeposit>().IgnoreQueryFilters().AnyAsync(t => t.AssetId == deposit.AssetId, cancellationToken));
    }

    [Fact]
    public async Task Remove_PaidOutDeposit_KeepsReturnAccountBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var paidOut = await client.CreatePaidOutDepositAsync(cancellationToken);
        var leg = Assert.Single(
            (await client.ListTransactionsAsync(paidOut.CashAssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Deposit && t.Transfer is not null);

        var response = await client.DeleteAsync(AssetUri(paidOut.Deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync(DepositUri(paidOut.Deposit.AssetId), cancellationToken)).StatusCode);
        Assert.Equal(1_000_119.83m, (await client.GetAssetAsync(paidOut.CashAssetId, cancellationToken)).Quantity);
        var kept = Assert.Single(
            (await client.ListTransactionsAsync(paidOut.CashAssetId, cancellationToken)).Items, t => t.Id == leg.Id);
        Assert.Equal(10_119.83m, kept.Quantity);
        Assert.Null(kept.Transfer);
        await using var dbContext = CreateDbContext(userId);
        Assert.Null((await dbContext.Transactions.SingleAsync(t => t.Id == leg.Id, cancellationToken)).TransferId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == paidOut.Deposit.AssetId, cancellationToken));
    }

    [Fact]
    public async Task Remove_SwappedBond_DetachesLegs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateSettledTosAsync(cancellationToken);
        await client.SwapBondAsync(funded.Bond.AssetId, 10, cancellationToken);
        var edo = await client.FindSwappedBondAsync(funded.Bond.AssetId, cancellationToken);

        var response = await client.DeleteAsync(AssetUri(funded.Bond.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await ReadBondRedemptionsAsync(userId, cancellationToken));
        var kept = await client.GetBondAsync(edo.AssetId, cancellationToken);
        Assert.Null(kept.SwappedFrom);
        Assert.Equal(999.00m, kept.BookValue);
        var opening = Assert.Single((await client.ListTransactionsAsync(edo.AssetId, cancellationToken)).Items);
        Assert.Equal(999.00m, opening.Quantity);
        Assert.Null(opening.Transfer);
        Assert.Equal(5_112.69m, (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
        var leftover = Assert.Single(
            (await client.ListTransactionsAsync(funded.CashAssetId, cancellationToken)).Items,
            t => t.Quantity == 112.69m);
        Assert.Null(leftover.Transfer);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == funded.Bond.AssetId, cancellationToken));
    }

    [Fact]
    public async Task Remove_DepositPaidIntoBankAccount_KeepsBankBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateDepositPaidIntoBankAsync(cancellationToken);
        var leg = Assert.Single((await client.ListTransactionsAsync(setup.Account.MoneyAssetId, cancellationToken)).Items);

        var response = await client.DeleteAsync(AssetUri(setup.Deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(setup.Account.MoneyAssetId, cancellationToken)).Quantity);
        var kept = Assert.Single((await client.ListTransactionsAsync(setup.Account.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal(leg.Id, kept.Id);
        Assert.Equal(TransactionType.Deposit, kept.Type);
        Assert.Equal(10_119.83m, kept.Quantity);
        Assert.Null(kept.Transfer);
        await using var dbContext = CreateDbContext(userId);
        Assert.Null((await dbContext.Transactions.SingleAsync(t => t.Id == leg.Id, cancellationToken)).TransferId);
    }

    [Fact]
    public async Task Remove_Bond_DeletesSettlements()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedBondAsync(cancellationToken, request: NewRorBondRequest());
        await client.SettleBondInterestAsync(
            funded.Bond.AssetId, [(1, null), (2, 3.75m)], cancellationToken);

        var response = await client.DeleteAsync(AssetUri(funded.Bond.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var dbContext = CreateDbContext(userId);
        Assert.False(await dbContext.Set<BondInterestSettlement>().IgnoreQueryFilters().AnyAsync(s => s.AssetId == funded.Bond.AssetId, cancellationToken));
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == funded.Bond.AssetId, cancellationToken));
        Assert.Equal(1_025.91m, (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.AssetId == funded.CashAssetId && t.TransferId != null, cancellationToken));
    }
}
