using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// Settle and undo are called directly and asserted on the raw response; PlanningApi helpers only arrange and read back.
public sealed class OneOffIncomeDepositEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Settle_WithDeposit_CreditsAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var income = await client.CreateOneOffIncomeAsync(account.AccountId, cancellationToken, "Premia", 2_500m, SeptemberLastDay);

        var response = await client.PutAsJsonAsync(
            OneOffIncomeSettlementUri(income),
            SettleOccurrenceBody(account.AccountId, 2_450m, SeptemberLastDay, createDeposit: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var deposit = Assert.Single((await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, deposit.Type);
        Assert.Equal(2_450m, deposit.Quantity);
        Assert.Equal(SeptemberLastDay, deposit.Date);
        Assert.Equal(2_450m, deposit.ValuePln);
        Assert.Equal(2_450m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));
        Assert.True((await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes.OneOff(income).DepositCreated);
    }

    [Fact]
    public async Task Edit_SyncsDeposit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.CreateOneOffIncomeDepositAsync(cancellationToken);
        var mbank = deposit.Account;
        var company = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe");
        var date = new DateOnly(2026, 9, 29);

        var changed = await client.PutAsJsonAsync(
            OneOffIncomeSettlementUri(deposit.IncomeId),
            SettleOccurrenceBody(mbank.AccountId, 2_600m, date, createDeposit: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        var edited = Assert.Single((await client.ListTransactionsAsync(mbank.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal(2_600m, edited.Quantity);
        Assert.Equal(date, edited.Date);
        Assert.Equal(2_600m, await client.GetCashBalanceAsync(mbank.AccountId, cancellationToken));

        var moved = await client.PutAsJsonAsync(
            OneOffIncomeSettlementUri(deposit.IncomeId),
            SettleOccurrenceBody(company.AccountId, 2_600m, date, createDeposit: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        Assert.Equal(0m, await client.GetCashBalanceAsync(mbank.AccountId, cancellationToken));
        Assert.Empty((await client.ListTransactionsAsync(mbank.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal(2_600m, Assert.Single((await client.ListTransactionsAsync(company.MoneyAssetId, cancellationToken)).Items).Quantity);

        var off = await client.PutAsJsonAsync(
            OneOffIncomeSettlementUri(deposit.IncomeId),
            SettleOccurrenceBody(company.AccountId, 2_600m, date, createDeposit: false),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        Assert.Empty((await client.ListTransactionsAsync(company.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal(0m, await client.GetCashBalanceAsync(company.AccountId, cancellationToken));
        var settled = (await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes.OneOff(deposit.IncomeId);
        Assert.Equal("Settled", settled.Status);
        Assert.False(settled.DepositCreated);

        var on = await client.PutAsJsonAsync(
            OneOffIncomeSettlementUri(deposit.IncomeId),
            SettleOccurrenceBody(company.AccountId, 2_600m, date, createDeposit: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, on.StatusCode);
        Assert.Equal(2_600m, Assert.Single((await client.ListTransactionsAsync(company.MoneyAssetId, cancellationToken)).Items).Quantity);
        Assert.True((await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes.OneOff(deposit.IncomeId).DepositCreated);
    }

    [Fact]
    public async Task Edit_WhenCashWouldGoNegative_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.CreateOneOffIncomeDepositAsync(cancellationToken);
        var mbank = deposit.Account;
        var company = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe");
        await client.RecordTransactionAsync(
            mbank.MoneyAssetId, TransactionType.Withdraw, 2_400m, new DateOnly(2026, 10, 1), cancellationToken, unitPrice: 1m);

        var lowered = await client.PutAsJsonAsync(
            OneOffIncomeSettlementUri(deposit.IncomeId),
            SettleOccurrenceBody(mbank.AccountId, 100m, SeptemberLastDay, createDeposit: true),
            cancellationToken);
        var moved = await client.PutAsJsonAsync(
            OneOffIncomeSettlementUri(deposit.IncomeId),
            SettleOccurrenceBody(company.AccountId, 2_500m, SeptemberLastDay, createDeposit: true),
            cancellationToken);
        var off = await client.PutAsJsonAsync(
            OneOffIncomeSettlementUri(deposit.IncomeId),
            SettleOccurrenceBody(mbank.AccountId, 2_500m, SeptemberLastDay, createDeposit: false),
            cancellationToken);
        var undo = await client.DeleteAsync(OneOffIncomeSettlementUri(deposit.IncomeId), cancellationToken);

        await lowered.AssertInsufficientFundsAsync(cancellationToken);
        await moved.AssertInsufficientFundsAsync(cancellationToken);
        await off.AssertInsufficientFundsAsync(cancellationToken);
        await undo.AssertInsufficientFundsAsync(cancellationToken);
        var stored = (await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes.OneOff(deposit.IncomeId);
        Assert.Equal("Settled", stored.Status);
        Assert.Equal(2_500m, stored.ActualAmount);
        Assert.True(stored.DepositCreated);
        var transactions = (await client.ListTransactionsAsync(mbank.MoneyAssetId, cancellationToken)).Items;
        Assert.Equal(2_500m, Assert.Single(transactions, t => t.Id == deposit.TransactionId).Quantity);
        Assert.Equal(100m, await client.GetCashBalanceAsync(mbank.AccountId, cancellationToken));
        Assert.Empty((await client.ListTransactionsAsync(company.MoneyAssetId, cancellationToken)).Items);
    }
}
