using System.Net;
using System.Net.Http.Json;
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

// The settle slice is called directly and asserted on the raw response; PlanningApi helpers only arrange and read back.
public sealed class OneOffExpenseSettlementEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static readonly DateOnly PlannedDate = new(2026, 10, 10);

    private static async Task<List<TransactionResponse>> WithdrawalsAsync(HttpClient client, Guid moneyAssetId, CancellationToken cancellationToken) =>
        (await client.ListTransactionsAsync(moneyAssetId, cancellationToken)).Items
            .Where(t => t.Type == TransactionType.Withdraw)
            .ToList();

    [Fact]
    public async Task Settle_CreatesWithdrawal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR", balance: 1_000m);
        var expense = await client.CreateOneOffExpenseAsync(cancellationToken, "Wakacje", 100m, PlannedDate);

        var response = await client.PutAsJsonAsync(
            OneOffExpenseSettlementUri(expense),
            SettleOneOffExpenseBody(account.AccountId, 90m, ExpenseSettlementDay, createWithdrawal: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(expense);
        Assert.Equal("Settled", row.Status);
        Assert.Equal(100m, row.Amount);
        Assert.Equal(PlannedDate, row.Date);
        Assert.Equal(90m, row.ActualAmount);
        Assert.Equal(ExpenseSettlementDay, row.ActualDate);
        Assert.Equal(90m, row.ActualAmountPln);
        Assert.Equal(-10m, row.Difference);
        Assert.True(row.WithdrawalCreated);
        var withdrawal = Assert.Single(await WithdrawalsAsync(client, account.MoneyAssetId, cancellationToken));
        Assert.Equal(90m, withdrawal.Quantity);
        Assert.Equal(ExpenseSettlementDay, withdrawal.Date);
        Assert.Equal(910m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Settle_RememberAccount_SetsExpenseAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR", balance: 1_000m);
        var expense = await client.CreateOneOffExpenseAsync(cancellationToken, "Wakacje", 100m, PlannedDate);

        var response = await client.PutAsJsonAsync(
            OneOffExpenseSettlementUri(expense),
            SettleOneOffExpenseBody(account.AccountId, 90m, ExpenseSettlementDay, rememberAccount: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(expense);
        Assert.Equal(account.AccountId, row.AccountId);
        Assert.Equal("mBank ROR", row.AccountName);
        Assert.False(row.WithdrawalCreated);
        Assert.Empty(await WithdrawalsAsync(client, account.MoneyAssetId, cancellationToken));
    }

    [Fact]
    public async Task Settle_ForeignCurrency_FreezesRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        Factory.FxRateLookupClient.WithRate("EUR", ExpenseSettlementDay, 3.6m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var eur = await client.CreateBankAccountAsync(cancellationToken, "Revolut EUR", currency: "EUR", balance: 100m);
        var expense = await client.CreateOneOffExpenseAsync(cancellationToken, "Wakacje", 100m, PlannedDate);

        var response = await client.PutAsJsonAsync(
            OneOffExpenseSettlementUri(expense),
            SettleOneOffExpenseBody(eur.AccountId, 25m, ExpenseSettlementDay, createWithdrawal: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(expense);
        Assert.Equal("EUR", row.Currency);
        Assert.Equal(25m, row.ActualAmount);
        Assert.Equal(90m, row.ActualAmountPln);
        Assert.Equal(-10m, row.Difference);
        var withdrawal = Assert.Single(await WithdrawalsAsync(client, eur.MoneyAssetId, cancellationToken));
        Assert.Equal(25m, withdrawal.Quantity);
        Assert.Equal(90m, withdrawal.ValuePln);
    }

    [Fact]
    public async Task Settle_NoRate_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        Factory.FxRateLookupClient.WithNotFound("EUR");
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var eur = await client.CreateBankAccountAsync(cancellationToken, "Revolut EUR", currency: "EUR", balance: 100m);
        var expense = await client.CreateOneOffExpenseAsync(cancellationToken, "Wakacje", 100m, PlannedDate);

        var response = await client.PutAsJsonAsync(
            OneOffExpenseSettlementUri(expense),
            SettleOneOffExpenseBody(eur.AccountId, 25m, ExpenseSettlementDay, createWithdrawal: true),
            cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, PortfolioAssertions.FxRateMissingErrorCode, cancellationToken);
        var row = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(expense);
        Assert.Equal("Pending", row.Status);
        Assert.Null(row.ActualAmount);
        Assert.Empty(await WithdrawalsAsync(client, eur.MoneyAssetId, cancellationToken));
    }

    [Fact]
    public async Task Settle_FutureDate_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR", balance: 1_000m);
        var expense = await client.CreateOneOffExpenseAsync(cancellationToken, "Wakacje", 100m, PlannedDate);

        var future = await client.PutAsJsonAsync(
            OneOffExpenseSettlementUri(expense),
            SettleOneOffExpenseBody(account.AccountId, 90m, new DateOnly(2026, 10, 21), createWithdrawal: true),
            cancellationToken);
        var zero = await client.PutAsJsonAsync(
            OneOffExpenseSettlementUri(expense),
            SettleOneOffExpenseBody(account.AccountId, 0m, ExpenseSettlementDay, createWithdrawal: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal("Pending", (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(expense).Status);
        Assert.Empty(await WithdrawalsAsync(client, account.MoneyAssetId, cancellationToken));
    }

    [Fact]
    public async Task Settle_InsufficientFunds_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR", balance: 50m);
        var expense = await client.CreateOneOffExpenseAsync(cancellationToken, "Wakacje", 100m, PlannedDate);

        var response = await client.PutAsJsonAsync(
            OneOffExpenseSettlementUri(expense),
            SettleOneOffExpenseBody(account.AccountId, 90m, ExpenseSettlementDay, createWithdrawal: true),
            cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        var row = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(expense);
        Assert.Equal("Pending", row.Status);
        Assert.Null(row.ActualAmount);
        Assert.Empty(await WithdrawalsAsync(client, account.MoneyAssetId, cancellationToken));
        Assert.Equal(50m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Settle_ArchivedAccount_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var archived = await client.CreateBankAccountAsync(cancellationToken, "Stare konto");
        await client.ArchiveAccountAsync(archived.AccountId, cancellationToken);
        var expense = await client.CreateOneOffExpenseAsync(cancellationToken, "Wakacje", 100m, PlannedDate);

        var response = await client.PutAsJsonAsync(
            OneOffExpenseSettlementUri(expense),
            SettleOneOffExpenseBody(archived.AccountId, 90m, ExpenseSettlementDay),
            cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountArchivedErrorCode, cancellationToken);
        Assert.Equal("Pending", (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(expense).Status);
    }

    [Fact]
    public async Task Resettle_MovesWithdrawal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settled = await client.CreateOneOffExpenseWithdrawalAsync(cancellationToken);
        var company = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe", balance: 1_000m);
        var newDate = new DateOnly(2026, 10, 16);

        var edited = await client.PutAsJsonAsync(
            OneOffExpenseSettlementUri(settled.ExpenseId),
            SettleOneOffExpenseBody(settled.Account.AccountId, 80m, newDate, createWithdrawal: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);
        var updated = Assert.Single(await WithdrawalsAsync(client, settled.Account.MoneyAssetId, cancellationToken));
        Assert.Equal((80m, newDate), (updated.Quantity, updated.Date));

        var moved = await client.PutAsJsonAsync(
            OneOffExpenseSettlementUri(settled.ExpenseId),
            SettleOneOffExpenseBody(company.AccountId, 80m, newDate, createWithdrawal: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        Assert.Empty(await WithdrawalsAsync(client, settled.Account.MoneyAssetId, cancellationToken));
        Assert.Equal(1_000m, await client.GetCashBalanceAsync(settled.Account.AccountId, cancellationToken));
        Assert.Equal(80m, Assert.Single(await WithdrawalsAsync(client, company.MoneyAssetId, cancellationToken)).Quantity);
        var row = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(settled.ExpenseId);
        Assert.Equal("Settled", row.Status);
        Assert.Equal(80m, row.ActualAmount);
        Assert.True(row.WithdrawalCreated);
    }

    [Fact]
    public async Task Resettle_WithoutWithdrawal_RemovesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settled = await client.CreateOneOffExpenseWithdrawalAsync(cancellationToken);

        var response = await client.PutAsJsonAsync(
            OneOffExpenseSettlementUri(settled.ExpenseId),
            SettleOneOffExpenseBody(settled.Account.AccountId, 90m, ExpenseSettlementDay, createWithdrawal: false),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await WithdrawalsAsync(client, settled.Account.MoneyAssetId, cancellationToken));
        Assert.Equal(1_000m, await client.GetCashBalanceAsync(settled.Account.AccountId, cancellationToken));
        var row = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(settled.ExpenseId);
        Assert.Equal("Settled", row.Status);
        Assert.False(row.WithdrawalCreated);
    }

    [Fact]
    public async Task Resettle_AfterManualDelete_CreatesNew()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settled = await client.CreateOneOffExpenseWithdrawalAsync(cancellationToken);
        var deleted = await client.DeleteAsync(TransactionUri(settled.Account.MoneyAssetId, settled.TransactionId), cancellationToken);
        Assert.True(deleted.IsSuccessStatusCode, $"delete returned {(int)deleted.StatusCode}.");

        var response = await client.PutAsJsonAsync(
            OneOffExpenseSettlementUri(settled.ExpenseId),
            SettleOneOffExpenseBody(settled.Account.AccountId, 90m, ExpenseSettlementDay, createWithdrawal: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var created = Assert.Single(await WithdrawalsAsync(client, settled.Account.MoneyAssetId, cancellationToken));
        Assert.NotEqual(settled.TransactionId, created.Id);
        Assert.Equal(90m, created.Quantity);
        Assert.True((await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(settled.ExpenseId).WithdrawalCreated);
    }

    [Fact]
    public async Task ManualWithdrawalEdit_KeepsSettlement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settled = await client.CreateOneOffExpenseWithdrawalAsync(cancellationToken);
        var update = new UpdateTransactionRequest
        {
            Type = TransactionType.Withdraw,
            Quantity = 95m,
            UnitPrice = 1m,
            Date = ExpenseSettlementDay
        };

        var response = await client.PutAsJsonAsync(
            TransactionUri(settled.Account.MoneyAssetId, settled.TransactionId), update, cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"edit returned {(int)response.StatusCode}.");
        var row = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(settled.ExpenseId);
        Assert.Equal("Settled", row.Status);
        Assert.Equal(90m, row.ActualAmount);
        Assert.True(row.WithdrawalCreated);
    }

    [Fact]
    public async Task ManualWithdrawalDelete_KeepsSettlement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settled = await client.CreateOneOffExpenseWithdrawalAsync(cancellationToken);

        var response = await client.DeleteAsync(
            TransactionUri(settled.Account.MoneyAssetId, settled.TransactionId), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"delete returned {(int)response.StatusCode}.");
        var row = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(settled.ExpenseId);
        Assert.Equal("Settled", row.Status);
        Assert.Equal(90m, row.ActualAmount);
        Assert.False(row.WithdrawalCreated);
    }
}
