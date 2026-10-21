using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class UpdateTransactionEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Update_ChangesQuantityAndRecomputesAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 15m, UnitPrice = 100m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(assetId, buyId), update, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken);
        Assert.Equal(15m, body!.Quantity);
        Assert.Equal(100m, body.UnitPrice);
        Assert.Equal(15m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(assetId, cancellationToken);
    }

    [Fact]
    public async Task Update_MetalTradeWithUnitPrice_ReturnsMetalTotalRequired()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateCashAndMetalAsync(cancellationToken);
        var buy = await client.RecordMetalTransactionWithCashAsync(setup.MetalAssetId, setup.CashAssetId, cancellationToken);
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 2m, UnitPrice = 1_300m, Date = buy.Date };

        var response = await client.PutAsJsonAsync(TransactionUri(setup.MetalAssetId, buy.Id), update, cancellationToken);

        await response.AssertMetalTotalRequiredAsync(cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }

    [Fact]
    public async Task Update_OldBuyDownwardThatBreaksLaterSell_ReturnsConflictAndNothingChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Sell, 8m, new DateOnly(2026, 1, 2), cancellationToken);
        // 10 - 8 = 2 today; dropping the Buy to 5 would let the Sell dip the running total to -3.
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 5m, UnitPrice = 100m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(assetId, buyId), update, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
        var page = await client.ListTransactionsAsync(assetId, cancellationToken);
        var untouchedBuy = page.Items.Single(t => t.Id == buyId);
        Assert.Equal(10m, untouchedBuy.Quantity);
    }

    [Fact]
    public async Task Update_ToDisallowedTypeOnCashAsset_ReturnsBadRequestAndNothingChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var assetId = await client.AddCashAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(
            assetId, TransactionType.Deposit, 1_000m, new DateOnly(2026, 1, 1), cancellationToken, unitPrice: 1m);
        var depositId = await client.RecordTransactionAsync(
            assetId, TransactionType.Deposit, 500m, new DateOnly(2026, 1, 2), cancellationToken, unitPrice: 1m);
        // A Sell of 100 after the opening 1000 would never oversell — only the type rule can reject it.
        var update = new UpdateTransactionRequest { Type = TransactionType.Sell, Quantity = 100m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 2) };

        var response = await client.PutAsJsonAsync(TransactionUri(assetId, depositId), update, cancellationToken);

        await response.AssertTransactionTypeNotAllowedAsync(cancellationToken);
        var stored = (await client.ListTransactionsAsync(assetId, cancellationToken)).Items.Single(t => t.Id == depositId);
        Assert.Equal(TransactionType.Deposit, stored.Type);
        Assert.Equal(500m, stored.Quantity);
        Assert.Equal(1m, stored.UnitPrice);
        Assert.Equal(new DateOnly(2026, 1, 2), stored.Date);
        Assert.Equal(1_500m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Update_BankAccountTransaction_MovesBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateSavingsBankAccountAsync(cancellationToken);
        var openingId = Assert.Single((await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items).Id;
        var edit = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 12_000m, UnitPrice = 1m, Date = FixedToday };

        var response = await client.PutAsJsonAsync(TransactionUri(account.MoneyAssetId, openingId), edit, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(12_000m, (await client.GetAssetAsync(account.MoneyAssetId, cancellationToken)).Quantity);
        Assert.Equal(12_000m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(account.MoneyAssetId, cancellationToken);

        var toBuy = edit with { Type = TransactionType.Buy };
        var refused = await client.PutAsJsonAsync(TransactionUri(account.MoneyAssetId, openingId), toBuy, cancellationToken);

        await refused.AssertTransactionTypeNotAllowedAsync(cancellationToken);
        Assert.Equal(12_000m, (await client.GetAssetAsync(account.MoneyAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Update_ForNonExistentTransaction_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(assetId, Guid.NewGuid()), update, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutToken_ReturnsUnauthorized()
    {
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };
        using var client = Factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            TransactionUri(Guid.NewGuid(), Guid.NewGuid()), update, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangedDate_ReResolvesRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalDate = new DateOnly(2026, 3, 2);
        var newDate = new DateOnly(2026, 3, 10);
        Factory.FxRateLookupClient
            .WithRate("EUR", originalDate, 4.30m)
            .WithRate("EUR", newDate, 4.50m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken, currency: "EUR");
        var buyId = await client.RecordTransactionAsync(
            assetId, TransactionType.Buy, 10m, originalDate, cancellationToken, unitPrice: 100m);
        Assert.Equal(4300.00m, (await client.ListTransactionsAsync(assetId, cancellationToken)).Items.Single().ValuePln);
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 10m, UnitPrice = 100m, Date = newDate };

        var response = await client.PutAsJsonAsync(TransactionUri(assetId, buyId), update, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("EUR", body.Currency);
        Assert.Equal(4500.00m, body.ValuePln);
        Assert.Equal(("EUR", newDate), Factory.FxRateLookupClient.Calls[^1]);
        Assert.Equal(4500.00m, (await client.ListTransactionsAsync(assetId, cancellationToken)).Items.Single().ValuePln);
    }

    [Fact]
    public async Task Update_OnArchivedAccount_ReturnsConflictWithoutFxLookup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("EUR", 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken, currency: "EUR");
        var buyId = await client.RecordTransactionAsync(assetId, TransactionType.Buy, 10m, new DateOnly(2026, 3, 2), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Sell, 10m, new DateOnly(2026, 3, 3), cancellationToken);
        await client.ArchiveAccountAsync(accountId, cancellationToken);
        var callsBeforeUpdate = Factory.FxRateLookupClient.Calls.Count;
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 15m, UnitPrice = 100m, Date = new DateOnly(2026, 3, 10) };

        var response = await client.PutAsJsonAsync(TransactionUri(assetId, buyId), update, cancellationToken);

        await response.AssertAccountArchivedConflictAsync(cancellationToken);
        Assert.Equal(callsBeforeUpdate, Factory.FxRateLookupClient.Calls.Count);
        var unchanged = (await client.ListTransactionsAsync(assetId, cancellationToken)).Items.Single(t => t.Id == buyId);
        Assert.Equal(10m, unchanged.Quantity);
        Assert.Equal(new DateOnly(2026, 3, 2), unchanged.Date);
    }

    [Fact]
    public async Task Update_ArchivedAsset_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(assetId, TransactionType.Buy, 100m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.ArchiveAssetAsync(assetId, cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 150m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(assetId, buyId), update, cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        Assert.Equal(100m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
        Assert.Equal(100m, (await client.ListTransactionsAsync(assetId, cancellationToken)).Items.Single(t => t.Id == buyId).Quantity);
    }

    [Fact]
    public async Task Update_InArchivedAccount_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var buyId = await client.RecordTransactionAsync(assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Sell, 10m, new DateOnly(2026, 1, 2), cancellationToken);
        await client.ArchiveAccountAsync(accountId, cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 15m, UnitPrice = 100m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(assetId, buyId), update, cancellationToken);

        await response.AssertAccountArchivedConflictAsync(cancellationToken);
        Assert.Equal(0m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
        var page = await client.ListTransactionsAsync(assetId, cancellationToken);
        Assert.Equal(10m, page.Items.Single(t => t.Id == buyId).Quantity);
    }

    [Fact]
    public async Task Update_TransferLeg_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateFundedDepositAsync(cancellationToken);
        var leg = await client.GetCashWithdrawAsync(funded.CashAssetId, cancellationToken);
        var legUpdate = new UpdateTransactionRequest { Type = TransactionType.Withdraw, Quantity = 200m, UnitPrice = 1m, Date = leg.Date };

        var response = await client.PutAsJsonAsync(
            TransactionUri(funded.CashAssetId, leg.Id), legUpdate, cancellationToken);

        await response.AssertTransferLegManagedAsync(cancellationToken);
        var unchanged = await client.GetCashWithdrawAsync(funded.CashAssetId, cancellationToken);
        Assert.Equal(leg.Id, unchanged.Id);
        Assert.Equal(1_000m, unchanged.Quantity);
        Assert.NotNull(unchanged.Transfer);
        Assert.Equal(4_000m, (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
        Assert.Equal(1_000m, (await client.GetAssetAsync(funded.Deposit.AssetId, cancellationToken)).Quantity);

        // Control: the plain top-up beside it is still an ordinary, editable transaction.
        var topUp = Assert.Single(
            (await client.ListTransactionsAsync(funded.CashAssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Deposit);
        var topUpUpdate = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 6_000m, UnitPrice = 1m, Date = topUp.Date };
        var topUpResponse = await client.PutAsJsonAsync(
            TransactionUri(funded.CashAssetId, topUp.Id), topUpUpdate, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, topUpResponse.StatusCode);
        Assert.Equal(5_000m, (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Update_OnTermDeposit_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);
        var openingId = Assert.Single((await client.ListTransactionsAsync(deposit.AssetId, cancellationToken)).Items).Id;
        var update = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 20_000m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 15) };

        var response = await client.PutAsJsonAsync(TransactionUri(deposit.AssetId, openingId), update, cancellationToken);

        await response.AssertDepositTransactionsManagedAsync(cancellationToken);
        Assert.Equal(10_000m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
        var opening = Assert.Single((await client.ListTransactionsAsync(deposit.AssetId, cancellationToken)).Items);
        Assert.Equal(openingId, opening.Id);
        Assert.Equal(10_000m, opening.Quantity);
    }

    [Fact]
    public async Task Update_BankInterestCredit_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateInterestAccountAsync(cancellationToken);
        await client.SettlePreviewedInterestAsync(account.AccountId, cancellationToken);
        var transactions = (await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items;
        var credit = Assert.Single(transactions, t => t.BankInterestPeriodEnd is not null);
        var opening = Assert.Single(transactions, t => t.BankInterestPeriodEnd is null);
        var update = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 99m, UnitPrice = 1m, Date = credit.Date };

        var response = await client.PutAsJsonAsync(TransactionUri(account.MoneyAssetId, credit.Id), update, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.BankInterestManagedErrorCode, cancellationToken);
        var unchanged = Assert.Single(
            (await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items, t => t.Id == credit.Id);
        Assert.Equal(33.29m, unchanged.Quantity);
        Assert.Equal(10_033.29m, (await client.GetAssetAsync(account.MoneyAssetId, cancellationToken)).Quantity);

        // Control: the ordinary opening Deposit is still editable.
        var openingUpdate = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 12_000m, UnitPrice = 1m, Date = opening.Date };
        var openingResponse = await client.PutAsJsonAsync(TransactionUri(account.MoneyAssetId, opening.Id), openingUpdate, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, openingResponse.StatusCode);
        Assert.Equal(12_033.29m, (await client.GetAssetAsync(account.MoneyAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task OneOffIncomeDeposit_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.CreateOneOffIncomeDepositAsync(cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 99m, UnitPrice = 1m, Date = SeptemberLastDay };

        var response = await client.PutAsJsonAsync(TransactionUri(deposit.Account.MoneyAssetId, deposit.TransactionId), update, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.IncomeDepositManagedErrorCode, cancellationToken);
        var unchanged = Assert.Single((await client.ListTransactionsAsync(deposit.Account.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal(2_500m, unchanged.Quantity);
    }

    [Fact]
    public async Task Update_IncomeDeposit_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.CreateIncomeDepositAsync(cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 99m, UnitPrice = 1m, Date = SeptemberLastDay };

        var response = await client.PutAsJsonAsync(TransactionUri(deposit.Account.MoneyAssetId, deposit.TransactionId), update, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.IncomeDepositManagedErrorCode, cancellationToken);
        var unchanged = Assert.Single((await client.ListTransactionsAsync(deposit.Account.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal(9_143.27m, unchanged.Quantity);
        Assert.Equal(9_143.27m, (await client.GetAssetAsync(deposit.Account.MoneyAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Update_TransactionInSettledPeriod_LeavesSettlementAsItIs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateInterestAccountAsync(cancellationToken);
        await client.SettlePreviewedInterestAsync(account.AccountId, cancellationToken);
        var settlementId = await client.GetLastSettlementIdAsync(account.AccountId, cancellationToken);
        var opening = Assert.Single(
            (await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items,
            t => t.BankInterestPeriodEnd is null);
        var update = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 12_000m, UnitPrice = 1m, Date = opening.Date };

        var response = await client.PutAsJsonAsync(TransactionUri(account.MoneyAssetId, opening.Id), update, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var last = (await client.GetAccountJsonAsync(account.AccountId, cancellationToken)).Bank().GetProperty("lastSettlement");
        Assert.Equal(settlementId, last.GetProperty("settlementId").GetGuid());
        Assert.Equal(41.10m, last.GetProperty("grossInterest").GetDecimal());
        Assert.Equal(7.81m, last.GetProperty("tax").GetDecimal());
        Assert.Equal(33.29m, last.GetProperty("netInterest").GetDecimal());
    }

    [Fact]
    public async Task Update_ManualTransferLeg_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var setup = await client.CreateCurrentAndSavingsAsync(cancellationToken);
        await client.CreateTransferAsync(cancellationToken, NewTransferRequest(setup.Current.MoneyAssetId, setup.Savings.MoneyAssetId, 2_000m));
        var leg = await client.GetCashWithdrawAsync(setup.Current.MoneyAssetId, cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Withdraw, Quantity = 200m, UnitPrice = 1m, Date = leg.Date };

        var response = await client.PutAsJsonAsync(
            TransactionUri(setup.Current.MoneyAssetId, leg.Id), update, cancellationToken);

        await response.AssertTransferLegManagedAsync(cancellationToken);
        var unchanged = await client.GetCashWithdrawAsync(setup.Current.MoneyAssetId, cancellationToken);
        Assert.Equal(2_000m, unchanged.Quantity);
        Assert.NotNull(unchanged.Transfer);
        Assert.Equal(3_000m, (await client.GetAssetAsync(setup.Current.MoneyAssetId, cancellationToken)).Quantity);
        Assert.Equal(2_000m, (await client.GetAssetAsync(setup.Savings.MoneyAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Update_DepositPayoutLegOnBankAccount_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var setup = await client.CreateDepositPaidIntoBankAsync(cancellationToken);
        var leg = Assert.Single((await client.ListTransactionsAsync(setup.Account.MoneyAssetId, cancellationToken)).Items);
        var update = new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 200m, UnitPrice = 1m, Date = leg.Date };

        var response = await client.PutAsJsonAsync(
            TransactionUri(setup.Account.MoneyAssetId, leg.Id), update, cancellationToken);

        await response.AssertTransferLegManagedAsync(cancellationToken);
        var unchanged = Assert.Single((await client.ListTransactionsAsync(setup.Account.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal(leg.Id, unchanged.Id);
        Assert.Equal(10_119.83m, unchanged.Quantity);
        Assert.NotNull(unchanged.Transfer);
        Assert.Equal(10_119.83m, (await client.GetAssetAsync(setup.Account.MoneyAssetId, cancellationToken)).Quantity);
        Assert.Equal(0m, (await client.GetAssetAsync(setup.Deposit.AssetId, cancellationToken)).Quantity);

        var delete = await client.DeleteAsync(
            TransactionUri(setup.Account.MoneyAssetId, leg.Id), cancellationToken);
        await delete.AssertTransferLegManagedAsync(cancellationToken);
    }

    [Fact]
    public async Task Update_SetsAndClearsAmountPln()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        Factory.FxRateLookupClient.WithRate("EUR", CashDay1, 4.00m);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken, currency: "EUR");
        var buy = await client.RecordBrokerageTradeAsync(
            holding.AssetId, cancellationToken, quantity: 10m, unitPrice: 100m, date: CashDay1, feeAmount: 5m);
        var withAmount = new UpdateTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 10m,
            UnitPrice = 100m,
            Date = CashDay1,
            FeeAmount = 5m,
            AmountPln = 4_312.50m
        };
        var withoutAmount = withAmount with { AmountPln = null };

        var set = await client.PutAsJsonAsync(TransactionUri(holding.AssetId, buy.Id), withAmount, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var setBody = (await set.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken))!;
        Assert.Equal(4_312.50m, setBody.AmountPln);
        Assert.Equal(4_312.50m, setBody.ValuePln);
        await using (var dbContext = CreateDbContext(userId))
        {
            var stored = await dbContext.Transactions.SingleAsync(t => t.Id == buy.Id, cancellationToken);
            Assert.Equal(4_312.50m, stored.AmountPln);
            Assert.Equal(4.3125m, stored.FxRateToPln);
        }

        var cleared = await client.PutAsJsonAsync(TransactionUri(holding.AssetId, buy.Id), withoutAmount, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var clearedBody = (await cleared.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken))!;
        Assert.Null(clearedBody.AmountPln);
        Assert.Equal(4_000m, clearedBody.ValuePln);
        await using (var dbContext = CreateDbContext(userId))
        {
            var stored = await dbContext.Transactions.SingleAsync(t => t.Id == buy.Id, cancellationToken);
            Assert.Null(stored.AmountPln);
            Assert.Equal(4.00m, stored.FxRateToPln);
        }
    }
}
