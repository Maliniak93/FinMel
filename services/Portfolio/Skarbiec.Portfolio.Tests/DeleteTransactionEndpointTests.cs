using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class DeleteTransactionEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Delete_FromFourTransactionHistory_RecomputesAssetCorrectly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 5m, new DateOnly(2026, 1, 2), cancellationToken);
        var sellId = await client.RecordTransactionAsync(assetId, TransactionType.Sell, 3m, new DateOnly(2026, 1, 3), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Dividend, 0m, new DateOnly(2026, 1, 4), cancellationToken);
        // 10 + 5 - 3 = 12 before the delete.

        var response = await client.DeleteAsync(TransactionUri(assetId, sellId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(15m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
        var page = await client.ListTransactionsAsync(assetId, cancellationToken);
        Assert.Equal(3, page.TotalCount);
        Assert.DoesNotContain(page.Items, t => t.Id == sellId);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(assetId, cancellationToken);
    }

    [Fact]
    public async Task Delete_ThatBreaksLaterSell_ReturnsConflictAndNothingChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Sell, 8m, new DateOnly(2026, 1, 2), cancellationToken);
        // Removing the Buy would leave a lone Sell 8 with nothing to sell from.

        var response = await client.DeleteAsync(TransactionUri(assetId, buyId), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
        var page = await client.ListTransactionsAsync(assetId, cancellationToken);
        Assert.Equal(2, page.TotalCount);
        Assert.Contains(page.Items, t => t.Id == buyId);
    }

    [Fact]
    public async Task Delete_TransferLeg_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedDepositAsync(cancellationToken);
        var leg = await client.GetCashWithdrawAsync(funded.CashAssetId, cancellationToken);

        var response = await client.DeleteAsync(
            TransactionUri(funded.CashAssetId, leg.Id), cancellationToken);

        await response.AssertTransferLegManagedAsync(cancellationToken);
        await AssertFundedDepositUnchangedAsync(client, userId, funded, cancellationToken);

        // Control: an ordinary Cash transaction is still deletable.
        var extraTopUpId = await client.RecordTransactionAsync(
            funded.CashAssetId, TransactionType.Deposit, 250m, new DateOnly(2026, 3, 1), cancellationToken, unitPrice: 1m);
        var extraResponse = await client.DeleteAsync(
            TransactionUri(funded.CashAssetId, extraTopUpId), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, extraResponse.StatusCode);
        Assert.Equal(4_000m, (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Delete_ForNonExistentTransaction_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);

        var response = await client.DeleteAsync(TransactionUri(assetId, Guid.NewGuid()), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.DeleteAsync(
            TransactionUri(Guid.NewGuid(), Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ArchivedAsset_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(assetId, TransactionType.Buy, 100m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.ArchiveAssetAsync(assetId, cancellationToken);

        var response = await client.DeleteAsync(TransactionUri(assetId, buyId), cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        Assert.Equal(100m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
        Assert.Contains((await client.ListTransactionsAsync(assetId, cancellationToken)).Items, t => t.Id == buyId);
    }

    [Fact]
    public async Task Delete_InArchivedAccount_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Sell, 10m, new DateOnly(2026, 1, 2), cancellationToken);
        await client.ArchiveAccountAsync(accountId, cancellationToken);

        var response = await client.DeleteAsync(TransactionUri(assetId, buyId), cancellationToken);

        await response.AssertAccountArchivedConflictAsync(cancellationToken);
        Assert.Equal(0m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
        var page = await client.ListTransactionsAsync(assetId, cancellationToken);
        Assert.Contains(page.Items, t => t.Id == buyId);
    }

    [Fact]
    public async Task Delete_OnTermDeposit_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);
        var openingId = Assert.Single((await client.ListTransactionsAsync(deposit.AssetId, cancellationToken)).Items).Id;

        var response = await client.DeleteAsync(TransactionUri(deposit.AssetId, openingId), cancellationToken);

        await response.AssertDepositTransactionsManagedAsync(cancellationToken);
        Assert.Equal(10_000m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
        Assert.Contains((await client.ListTransactionsAsync(deposit.AssetId, cancellationToken)).Items, t => t.Id == openingId);
    }

    [Fact]
    public async Task Delete_BankAccountTransaction_MovesBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateSavingsBankAccountAsync(cancellationToken);
        var withdrawId = await client.RecordTransactionAsync(
            account.MoneyAssetId, TransactionType.Withdraw, 4_000m, FixedToday, cancellationToken, unitPrice: 1m);
        Assert.Equal(6_000m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));

        var response = await client.DeleteAsync(TransactionUri(account.MoneyAssetId, withdrawId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(10_000m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(account.MoneyAssetId, cancellationToken);
    }

    [Fact]
    public async Task Delete_BankInterestCredit_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var account = await client.CreateInterestAccountAsync(cancellationToken);
        await client.SettlePreviewedInterestAsync(account.AccountId, cancellationToken);
        var credit = Assert.Single(
            (await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items,
            t => t.BankInterestPeriodEnd is not null);

        var response = await client.DeleteAsync(TransactionUri(account.MoneyAssetId, credit.Id), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.BankInterestManagedErrorCode, cancellationToken);
        Assert.Equal(10_033.29m, (await client.GetAssetAsync(account.MoneyAssetId, cancellationToken)).Quantity);
        Assert.Contains((await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items, t => t.Id == credit.Id);
        Assert.Equal(1, await CountBankSettlementsAsync(userId, cancellationToken, account.MoneyAssetId));
    }

    [Fact]
    public async Task OneOffIncomeDeposit_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.CreateOneOffIncomeDepositAsync(cancellationToken);

        var response = await client.DeleteAsync(TransactionUri(deposit.Account.MoneyAssetId, deposit.TransactionId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.IncomeDepositManagedErrorCode, cancellationToken);
        Assert.Contains((await client.ListTransactionsAsync(deposit.Account.MoneyAssetId, cancellationToken)).Items, t => t.Id == deposit.TransactionId);
        Assert.Equal(2_500m, (await client.GetAssetAsync(deposit.Account.MoneyAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Delete_IncomeDeposit_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.CreateIncomeDepositAsync(cancellationToken);

        var response = await client.DeleteAsync(TransactionUri(deposit.Account.MoneyAssetId, deposit.TransactionId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.IncomeDepositManagedErrorCode, cancellationToken);
        Assert.Contains((await client.ListTransactionsAsync(deposit.Account.MoneyAssetId, cancellationToken)).Items, t => t.Id == deposit.TransactionId);
        Assert.Equal(9_143.27m, (await client.GetAssetAsync(deposit.Account.MoneyAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Delete_OrdinaryBankWithdrawAfterSettlement_StaysAllowed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateInterestAccountAsync(cancellationToken);
        await client.SettlePreviewedInterestAsync(account.AccountId, cancellationToken);
        var withdrawId = await client.RecordTransactionAsync(
            account.MoneyAssetId, TransactionType.Withdraw, 500m, new DateOnly(2026, 10, 1), cancellationToken, unitPrice: 1m);

        var response = await client.DeleteAsync(TransactionUri(account.MoneyAssetId, withdrawId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(10_033.29m, (await client.GetAssetAsync(account.MoneyAssetId, cancellationToken)).Quantity);
    }
}
