using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// Settle and undo are called directly and asserted on the raw response; PlanningApi helpers only arrange and read back.
public sealed class RecurringIncomeDepositEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static readonly DateTimeOffset MidNovemberUtc = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Settle_WithDeposit_CreditsAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var income = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Wypłata Dominik", 9_200m, new DateOnly(2026, 1, 1),
            dayKind: RecurrenceDayKind.LastBusinessDay, dayOfMonth: null);

        var response = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-09"),
            SettleOccurrenceBody(account.AccountId, 9_143.27m, SeptemberLastDay, createDeposit: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var deposit = Assert.Single((await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, deposit.Type);
        Assert.Equal(9_143.27m, deposit.Quantity);
        Assert.Equal(SeptemberLastDay, deposit.Date);
        Assert.Equal(9_143.27m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));
        var september = (await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes.Occurrence("2026-09");
        Assert.True(september.DepositCreated);
    }

    [Fact]
    public async Task ForeignDeposit_UsesFrozenRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MidNovemberUtc);
        Factory.InstrumentQuoteLookupClient.WithRate("EUR", 4.25m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var eur = await client.CreateBankAccountAsync(cancellationToken, "Revolut EUR", currency: "EUR");
        var income = await client.CreateRecurringIncomeAsync(eur.AccountId, cancellationToken, "Najem EUR", 1_000m, dayOfMonth: 10);
        var settleDate = new DateOnly(2026, 11, 10);
        Factory.FxRateLookupClient.WithRate("EUR", settleDate, 4.30m);

        var response = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-11"),
            SettleOccurrenceBody(eur.AccountId, 980m, settleDate, createDeposit: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var deposit = Assert.Single((await client.ListTransactionsAsync(eur.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal("EUR", deposit.Currency);
        Assert.Equal(980m, deposit.Quantity);
        Assert.Equal(4_214m, deposit.ValuePln);
    }

    [Fact]
    public async Task History_LabelsDepositWithIncomeName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.CreateIncomeDepositAsync(cancellationToken);
        var manualId = await client.RecordTransactionAsync(
            deposit.Account.MoneyAssetId, TransactionType.Deposit, 50m, new DateOnly(2026, 9, 1), cancellationToken, unitPrice: 1m);

        var labelled = (await client.ListTransactionsAsync(deposit.Account.MoneyAssetId, cancellationToken)).Items;

        Assert.Equal("Wypłata Dominik", Assert.Single(labelled, t => t.Id == deposit.TransactionId).RecurringIncomeName);
        Assert.Null(Assert.Single(labelled, t => t.Id == manualId).RecurringIncomeName);

        await client.UpdateRecurringIncomeAsync(
            deposit.IncomeId, deposit.Account.AccountId, "2026-10", cancellationToken, "Wypłata", 9_200m, new DateOnly(2026, 1, 1),
            dayKind: RecurrenceDayKind.LastBusinessDay, dayOfMonth: null);

        var renamed = (await client.ListTransactionsAsync(deposit.Account.MoneyAssetId, cancellationToken)).Items;
        Assert.Equal("Wypłata", Assert.Single(renamed, t => t.Id == deposit.TransactionId).RecurringIncomeName);
    }

    [Fact]
    public async Task Edit_SyncsDeposit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.CreateIncomeDepositAsync(cancellationToken);
        var mbank = deposit.Account;
        var company = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe");
        var date = new DateOnly(2026, 9, 29);

        var changed = await client.PutAsJsonAsync(
            SettleOccurrenceUri(deposit.IncomeId, "2026-09"),
            SettleOccurrenceBody(mbank.AccountId, 9_200m, date, createDeposit: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        var edited = Assert.Single((await client.ListTransactionsAsync(mbank.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal(9_200m, edited.Quantity);
        Assert.Equal(date, edited.Date);
        Assert.Equal(9_200m, await client.GetCashBalanceAsync(mbank.AccountId, cancellationToken));

        var moved = await client.PutAsJsonAsync(
            SettleOccurrenceUri(deposit.IncomeId, "2026-09"),
            SettleOccurrenceBody(company.AccountId, 9_200m, date, createDeposit: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        Assert.Equal(0m, await client.GetCashBalanceAsync(mbank.AccountId, cancellationToken));
        Assert.Empty((await client.ListTransactionsAsync(mbank.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal(9_200m, await client.GetCashBalanceAsync(company.AccountId, cancellationToken));
        Assert.Equal(9_200m, Assert.Single((await client.ListTransactionsAsync(company.MoneyAssetId, cancellationToken)).Items).Quantity);

        var off = await client.PutAsJsonAsync(
            SettleOccurrenceUri(deposit.IncomeId, "2026-09"),
            SettleOccurrenceBody(company.AccountId, 9_200m, date, createDeposit: false),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);
        Assert.Empty((await client.ListTransactionsAsync(company.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal(0m, await client.GetCashBalanceAsync(company.AccountId, cancellationToken));
        var settled = (await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes.Occurrence("2026-09");
        Assert.Equal("Settled", settled.Status);
        Assert.False(settled.DepositCreated);

        var on = await client.PutAsJsonAsync(
            SettleOccurrenceUri(deposit.IncomeId, "2026-09"),
            SettleOccurrenceBody(company.AccountId, 9_200m, date, createDeposit: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, on.StatusCode);
        Assert.Equal(9_200m, Assert.Single((await client.ListTransactionsAsync(company.MoneyAssetId, cancellationToken)).Items).Quantity);
        Assert.True((await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes.Occurrence("2026-09").DepositCreated);

        var undo = await client.DeleteAsync(OccurrenceUri(deposit.IncomeId, "2026-09"), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, undo.StatusCode);
        Assert.Empty((await client.ListTransactionsAsync(company.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal(0m, await client.GetCashBalanceAsync(company.AccountId, cancellationToken));
        Assert.Equal("Pending", (await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes.Occurrence("2026-09").Status);
    }

    [Fact]
    public async Task Edit_WhenCashWouldGoNegative_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.CreateIncomeDepositAsync(cancellationToken);
        var mbank = deposit.Account;
        var company = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe");
        await client.RecordTransactionAsync(
            mbank.MoneyAssetId, TransactionType.Withdraw, 9_000m, new DateOnly(2026, 10, 1), cancellationToken, unitPrice: 1m);

        var lowered = await client.PutAsJsonAsync(
            SettleOccurrenceUri(deposit.IncomeId, "2026-09"),
            SettleOccurrenceBody(mbank.AccountId, 100m, SeptemberLastDay, createDeposit: true),
            cancellationToken);
        var moved = await client.PutAsJsonAsync(
            SettleOccurrenceUri(deposit.IncomeId, "2026-09"),
            SettleOccurrenceBody(company.AccountId, 9_143.27m, SeptemberLastDay, createDeposit: true),
            cancellationToken);
        var off = await client.PutAsJsonAsync(
            SettleOccurrenceUri(deposit.IncomeId, "2026-09"),
            SettleOccurrenceBody(mbank.AccountId, 9_143.27m, SeptemberLastDay, createDeposit: false),
            cancellationToken);
        var undo = await client.DeleteAsync(OccurrenceUri(deposit.IncomeId, "2026-09"), cancellationToken);

        await lowered.AssertInsufficientFundsAsync(cancellationToken);
        await moved.AssertInsufficientFundsAsync(cancellationToken);
        await off.AssertInsufficientFundsAsync(cancellationToken);
        await undo.AssertInsufficientFundsAsync(cancellationToken);
        var stored = (await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes.Occurrence("2026-09");
        Assert.Equal("Settled", stored.Status);
        Assert.Equal(9_143.27m, stored.ActualAmount);
        Assert.Equal(mbank.AccountId, stored.AccountId);
        Assert.True(stored.DepositCreated);
        var transactions = (await client.ListTransactionsAsync(mbank.MoneyAssetId, cancellationToken)).Items;
        Assert.Equal(9_143.27m, Assert.Single(transactions, t => t.Id == deposit.TransactionId).Quantity);
        Assert.Equal(143.27m, await client.GetCashBalanceAsync(mbank.AccountId, cancellationToken));
        Assert.Empty((await client.ListTransactionsAsync(company.MoneyAssetId, cancellationToken)).Items);
    }

    [Fact]
    public async Task ArchivedAccount_Rules()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var planned = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var income = await client.CreateRecurringIncomeAsync(planned.AccountId, cancellationToken, "Pensja", 500m, new DateOnly(2026, 1, 1), dayOfMonth: 10);
        var archived = await client.CreateBankAccountAsync(cancellationToken, "Stare konto");
        await client.ArchiveAccountAsync(archived.AccountId, cancellationToken);

        var created = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-10"),
            SettleOccurrenceBody(archived.AccountId, 500m, new DateOnly(2026, 10, 10), createDeposit: true),
            cancellationToken);

        await created.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountArchivedErrorCode, cancellationToken);
        Assert.Equal("Pending", (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Occurrence("2026-10").Status);

        var moveSource = await client.CreateBankAccountAsync(cancellationToken, "Konto bieżące");
        await client.SettleOccurrenceAsync(
            income, "2026-08", moveSource.AccountId, 500m, new DateOnly(2026, 8, 10), cancellationToken, createDeposit: true);

        var moved = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-08"),
            SettleOccurrenceBody(archived.AccountId, 500m, new DateOnly(2026, 8, 10), createDeposit: true),
            cancellationToken);

        await moved.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountArchivedErrorCode, cancellationToken);
        Assert.Equal(500m, await client.GetCashBalanceAsync(moveSource.AccountId, cancellationToken));

        var later = await client.CreateBankAccountAsync(cancellationToken, "Konto do archiwum");
        await client.SettleOccurrenceAsync(income, "2026-07", later.AccountId, 400m, new DateOnly(2026, 7, 10), cancellationToken);
        await client.ArchiveAccountAsync(later.AccountId, cancellationToken);

        var amountOnly = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-07"),
            SettleOccurrenceBody(later.AccountId, 450m, new DateOnly(2026, 7, 10), createDeposit: false),
            cancellationToken);
        var depositOnArchived = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-07"),
            SettleOccurrenceBody(later.AccountId, 450m, new DateOnly(2026, 7, 10), createDeposit: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, amountOnly.StatusCode);
        await depositOnArchived.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountArchivedErrorCode, cancellationToken);
        var july = (await client.GetPlanningMonthAsync("2026-07", cancellationToken)).Incomes.Occurrence("2026-07");
        Assert.Equal(450m, july.ActualAmount);
        Assert.False(july.DepositCreated);

        var kept = await client.CreateBankAccountAsync(cancellationToken, "Konto z wpłatą");
        await client.SettleOccurrenceAsync(
            income, "2026-09", kept.AccountId, 500m, new DateOnly(2026, 9, 10), cancellationToken, createDeposit: true);
        await client.ArchiveAccountAsync(kept.AccountId, cancellationToken);

        var changeDeposit = await client.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-09"),
            SettleOccurrenceBody(kept.AccountId, 600m, new DateOnly(2026, 9, 10), createDeposit: true),
            cancellationToken);

        await changeDeposit.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountArchivedErrorCode, cancellationToken);
        Assert.Equal(500m, (await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes.Occurrence("2026-09").ActualAmount);
    }
}
