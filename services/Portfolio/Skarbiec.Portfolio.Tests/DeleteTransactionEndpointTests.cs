using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class DeleteTransactionEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Delete_FromFourTransactionHistory_RecomputesAssetCorrectly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 5m, new DateOnly(2026, 1, 2), cancellationToken);
        var sellId = await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Sell, 3m, new DateOnly(2026, 1, 3), cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Dividend, 0m, new DateOnly(2026, 1, 4), cancellationToken);
        // 10 + 5 - 3 = 12 before the delete.

        var response = await client.DeleteAsync(TransactionUri(portfolioId, assetId, sellId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(15m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        var page = await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(3, page.TotalCount);
        Assert.DoesNotContain(page.Items, t => t.Id == sellId);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, assetId, cancellationToken);
    }

    [Fact]
    public async Task Delete_ThatBreaksLaterSell_ReturnsConflictAndNothingChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Sell, 8m, new DateOnly(2026, 1, 2), cancellationToken);
        // Removing the Buy would leave a lone Sell 8 with nothing to sell from.

        var response = await client.DeleteAsync(TransactionUri(portfolioId, assetId, buyId), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        var page = await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(2, page.TotalCount);
        Assert.Contains(page.Items, t => t.Id == buyId);
    }

    /// <summary>
    /// asset-transfers-deposit-funding AC-6 (delete half): the Cash Withdraw leg of a transfer cannot
    /// be deleted on its own — 409 <c>Conflict.TransferLegManaged</c>, both legs stay and both
    /// quantities hold — while the ordinary top-up beside it stays deletable (as far as history allows).
    /// </summary>
    [Fact]
    public async Task Delete_TransferLeg_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedDepositAsync(cancellationToken);
        var leg = await client.GetCashWithdrawAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);

        var response = await client.DeleteAsync(
            TransactionUri(funded.CashPortfolioId, funded.CashAssetId, leg.Id), cancellationToken);

        await response.AssertTransferLegManagedAsync(cancellationToken);
        await AssertFundedDepositUnchangedAsync(client, userId, funded, cancellationToken);

        // Control: an ordinary Cash transaction is still deletable.
        var extraTopUpId = await client.RecordTransactionAsync(
            funded.CashPortfolioId, funded.CashAssetId, TransactionType.Deposit, 250m, new DateOnly(2026, 3, 1), cancellationToken, unitPrice: 1m);
        var extraResponse = await client.DeleteAsync(
            TransactionUri(funded.CashPortfolioId, funded.CashAssetId, extraTopUpId), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, extraResponse.StatusCode);
        Assert.Equal(4_000m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Delete_ForNonExistentTransaction_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);

        var response = await client.DeleteAsync(TransactionUri(portfolioId, assetId, Guid.NewGuid()), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AssetUnderWrongPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        var otherPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Other portfolio");

        var response = await client.DeleteAsync(TransactionUri(otherPortfolioId, assetId, buyId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.DeleteAsync(
            TransactionUri(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>asset-archive AC-4: deleting a transaction of an archived asset is a 409 <c>Conflict.AssetArchived</c>; the transaction stays.</summary>
    [Fact]
    public async Task Delete_ArchivedAsset_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var cashId = await client.AddCashAssetAsync(portfolioId, cancellationToken);
        var depositId = await client.RecordTransactionAsync(
            portfolioId, cashId, TransactionType.Deposit, 100m, new DateOnly(2026, 1, 1), cancellationToken, unitPrice: 1m);
        await client.ArchiveAssetAsync(portfolioId, cashId, cancellationToken);

        var response = await client.DeleteAsync(TransactionUri(portfolioId, cashId, depositId), cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        Assert.Equal(100m, (await client.GetAssetAsync(portfolioId, cashId, cancellationToken)).Quantity);
        Assert.Contains((await client.ListTransactionsAsync(portfolioId, cashId, cancellationToken)).Items, t => t.Id == depositId);
    }

    /// <summary>archived-portfolio-out-of-net-worth AC7: deleting a transaction of an archived
    /// portfolio's asset is a 409 <c>Conflict.PortfolioArchived</c>; transaction and quantity stay.</summary>
    [Fact]
    public async Task Delete_InArchivedPortfolio_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);

        var response = await client.DeleteAsync(TransactionUri(portfolioId, assetId, buyId), cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        Assert.Equal(10m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        var page = await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken);
        Assert.Contains(page.Items, t => t.Id == buyId);
    }

    /// <summary>term-deposits AC-10: deleting a term deposit's opening transaction is a 409
    /// <c>Conflict.DepositTransactionsManaged</c> — the deposit is removed as a whole, through
    /// <c>DELETE .../assets/{id}</c>, and nothing changes here.</summary>
    [Fact]
    public async Task Delete_OnTermDeposit_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var openingId = Assert.Single((await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items).Id;

        var response = await client.DeleteAsync(TransactionUri(portfolioId, deposit.AssetId, openingId), cancellationToken);

        await response.AssertDepositTransactionsManagedAsync(cancellationToken);
        Assert.Equal(10_000m, (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Quantity);
        Assert.Contains((await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).Items, t => t.Id == openingId);
    }

    /// <summary>savings-accounts AC-7: deleting a savings account's Withdraw gives the money back to the balance.</summary>
    [Fact]
    public async Task Delete_SavingsAccountTransaction_MovesBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken);
        var withdrawId = await client.RecordTransactionAsync(
            portfolioId, account.AssetId, TransactionType.Withdraw, 4_000m, SavingsToday, cancellationToken, unitPrice: 1m);
        Assert.Equal(6_000m, (await client.GetSavingsAccountAsync(portfolioId, account.AssetId, cancellationToken)).Balance);

        var response = await client.DeleteAsync(TransactionUri(portfolioId, account.AssetId, withdrawId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(10_000m, (await client.GetSavingsAccountAsync(portfolioId, account.AssetId, cancellationToken)).Balance);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, account.AssetId, cancellationToken);
    }

    /// <summary>
    /// savings-interest-settlement AC-9: the credit a settlement created is removed only by undoing
    /// the settlement - deleting it here is a 409 <c>Conflict.SavingsInterestManaged</c>, and the
    /// credit, the settlement and the balance stay.
    /// </summary>
    [Fact]
    public async Task Delete_SavingsInterestCredit_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        await client.SettlePreviewedSavingsInterestAsync(portfolioId, account.AssetId, cancellationToken);
        var credit = Assert.Single(
            (await client.ListTransactionsAsync(portfolioId, account.AssetId, cancellationToken)).Items,
            t => t.SavingsInterestPeriodEnd is not null);

        var response = await client.DeleteAsync(TransactionUri(portfolioId, account.AssetId, credit.Id), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.SavingsInterestManagedErrorCode, cancellationToken);
        Assert.Equal(10_033.29m, (await client.GetAssetAsync(portfolioId, account.AssetId, cancellationToken)).Quantity);
        Assert.Contains((await client.ListTransactionsAsync(portfolioId, account.AssetId, cancellationToken)).Items, t => t.Id == credit.Id);
        Assert.Equal(1, await CountSavingsSettlementsAsync(userId, cancellationToken, account.AssetId));
    }

    /// <summary>savings-interest-settlement AC-9: an ordinary savings Withdraw stays deletable even after a settlement.</summary>
    [Fact]
    public async Task Delete_OrdinarySavingsWithdrawAfterSettlement_StaysAllowed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        await client.SettlePreviewedSavingsInterestAsync(portfolioId, account.AssetId, cancellationToken);
        var withdrawId = await client.RecordTransactionAsync(
            portfolioId, account.AssetId, TransactionType.Withdraw, 500m, new DateOnly(2026, 10, 1), cancellationToken, unitPrice: 1m);

        var response = await client.DeleteAsync(TransactionUri(portfolioId, account.AssetId, withdrawId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(10_033.29m, (await client.GetAssetAsync(portfolioId, account.AssetId, cancellationToken)).Quantity);
    }
}
