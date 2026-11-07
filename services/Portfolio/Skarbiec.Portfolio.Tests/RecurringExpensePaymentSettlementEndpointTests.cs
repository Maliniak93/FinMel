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

// The settle and undo slices are called directly and asserted on the raw response; PlanningApi helpers only arrange and read back.
public sealed class RecurringExpensePaymentSettlementEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private const string October = "2026-10";

    private static async Task<List<TransactionResponse>> WithdrawalsAsync(HttpClient client, Guid moneyAssetId, CancellationToken cancellationToken) =>
        (await client.ListTransactionsAsync(moneyAssetId, cancellationToken)).Items
            .Where(t => t.Type == TransactionType.Withdraw)
            .ToList();

    private static async Task<ExpensePaymentView> OctoberPaymentAsync(HttpClient client, Guid expenseId, CancellationToken cancellationToken) =>
        (await client.GetPlanningMonthAsync(October, cancellationToken)).Expenses.Payment(expenseId, October);

    [Fact]
    public async Task Settle_CreatesRowAndWithdrawal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR", balance: 5_000m);
        var expense = await client.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 2_400m, dayOfMonth: 5);

        var response = await client.PutAsJsonAsync(
            ExpensePaymentSettlementUri(expense, October),
            SettleExpensePaymentBody(account.AccountId, 2_350m, ExpenseSettlementDay, createWithdrawal: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var payment = await OctoberPaymentAsync(client, expense, cancellationToken);
        Assert.Equal("Settled", payment.Status);
        Assert.Equal(2_400m, payment.Amount);
        Assert.Equal(new DateOnly(2026, 10, 5), payment.Date);
        Assert.Equal(2_350m, payment.ActualAmount);
        Assert.Equal(ExpenseSettlementDay, payment.ActualDate);
        Assert.Equal(2_350m, payment.ActualAmountPln);
        Assert.Equal(-50m, payment.Difference);
        Assert.Equal((account.AccountId, "mBank ROR"), (payment.AccountId, payment.AccountName));
        Assert.True(payment.WithdrawalCreated);
        var withdrawal = Assert.Single(await WithdrawalsAsync(client, account.MoneyAssetId, cancellationToken));
        Assert.Equal(2_350m, withdrawal.Quantity);
        Assert.Equal(ExpenseSettlementDay, withdrawal.Date);
        Assert.Equal(2_650m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Settle_ForeignCurrency_FreezesRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        Factory.FxRateLookupClient.WithRate("EUR", ExpenseSettlementDay, 3.6m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var eur = await client.CreateBankAccountAsync(cancellationToken, "Revolut EUR", currency: "EUR", balance: 100m);
        var expense = await client.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 100m, dayOfMonth: 5);

        var response = await client.PutAsJsonAsync(
            ExpensePaymentSettlementUri(expense, October),
            SettleExpensePaymentBody(eur.AccountId, 25m, ExpenseSettlementDay, createWithdrawal: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var payment = await OctoberPaymentAsync(client, expense, cancellationToken);
        Assert.Equal("EUR", payment.Currency);
        Assert.Equal(25m, payment.ActualAmount);
        Assert.Equal(90m, payment.ActualAmountPln);
        Assert.Equal(-10m, payment.Difference);
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
        var expense = await client.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 100m, dayOfMonth: 5);

        var response = await client.PutAsJsonAsync(
            ExpensePaymentSettlementUri(expense, October),
            SettleExpensePaymentBody(eur.AccountId, 25m, ExpenseSettlementDay, createWithdrawal: true),
            cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, PortfolioAssertions.FxRateMissingErrorCode, cancellationToken);
        var payment = await OctoberPaymentAsync(client, expense, cancellationToken);
        Assert.Equal("Pending", payment.Status);
        Assert.Null(payment.ActualAmount);
        Assert.Empty(await WithdrawalsAsync(client, eur.MoneyAssetId, cancellationToken));
    }

    [Fact]
    public async Task Settle_NotAPaymentMonth_Returns404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR", balance: 5_000m);
        var expense = await client.CreateRecurringExpenseAsync(
            cancellationToken, "Ubezpieczenie", 1_200m, intervalMonths: 3, dayOfMonth: 5);

        var response = await client.PutAsJsonAsync(
            ExpensePaymentSettlementUri(expense, "2026-11"),
            SettleExpensePaymentBody(account.AccountId, 1_200m, ExpenseSettlementDay, createWithdrawal: true),
            cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.NotFound, ExpensePaymentNotFoundErrorCode, cancellationToken);
        Assert.Empty(await WithdrawalsAsync(client, account.MoneyAssetId, cancellationToken));
    }

    [Fact]
    public async Task Settle_FutureDate_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR", balance: 5_000m);
        var expense = await client.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 2_400m, dayOfMonth: 5);

        var future = await client.PutAsJsonAsync(
            ExpensePaymentSettlementUri(expense, October),
            SettleExpensePaymentBody(account.AccountId, 2_350m, new DateOnly(2026, 10, 21), createWithdrawal: true),
            cancellationToken);
        var zero = await client.PutAsJsonAsync(
            ExpensePaymentSettlementUri(expense, October),
            SettleExpensePaymentBody(account.AccountId, 0m, ExpenseSettlementDay, createWithdrawal: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal("Pending", (await OctoberPaymentAsync(client, expense, cancellationToken)).Status);
        Assert.Empty(await WithdrawalsAsync(client, account.MoneyAssetId, cancellationToken));
    }

    [Fact]
    public async Task Settle_InsufficientFunds_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR", balance: 50m);
        var expense = await client.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 2_400m, dayOfMonth: 5);

        var response = await client.PutAsJsonAsync(
            ExpensePaymentSettlementUri(expense, October),
            SettleExpensePaymentBody(account.AccountId, 2_350m, ExpenseSettlementDay, createWithdrawal: true),
            cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        var payment = await OctoberPaymentAsync(client, expense, cancellationToken);
        Assert.Equal("Pending", payment.Status);
        Assert.Null(payment.ActualAmount);
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
        var expense = await client.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 2_400m, dayOfMonth: 5);

        var response = await client.PutAsJsonAsync(
            ExpensePaymentSettlementUri(expense, October),
            SettleExpensePaymentBody(archived.AccountId, 2_350m, ExpenseSettlementDay),
            cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountArchivedErrorCode, cancellationToken);
        Assert.Equal("Pending", (await OctoberPaymentAsync(client, expense, cancellationToken)).Status);
    }

    [Fact]
    public async Task Resettle_MovesWithdrawal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settled = await client.CreateExpensePaymentWithdrawalAsync(cancellationToken);
        var company = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe", balance: 5_000m);
        var newDate = new DateOnly(2026, 10, 16);

        var edited = await client.PutAsJsonAsync(
            ExpensePaymentSettlementUri(settled.ExpenseId, October),
            SettleExpensePaymentBody(settled.Account.AccountId, 2_300m, newDate, createWithdrawal: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);
        var updated = Assert.Single(await WithdrawalsAsync(client, settled.Account.MoneyAssetId, cancellationToken));
        Assert.Equal((2_300m, newDate), (updated.Quantity, updated.Date));

        var moved = await client.PutAsJsonAsync(
            ExpensePaymentSettlementUri(settled.ExpenseId, October),
            SettleExpensePaymentBody(company.AccountId, 2_300m, newDate, createWithdrawal: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        Assert.Empty(await WithdrawalsAsync(client, settled.Account.MoneyAssetId, cancellationToken));
        Assert.Equal(5_000m, await client.GetCashBalanceAsync(settled.Account.AccountId, cancellationToken));
        Assert.Equal(2_300m, Assert.Single(await WithdrawalsAsync(client, company.MoneyAssetId, cancellationToken)).Quantity);
        var payment = await OctoberPaymentAsync(client, settled.ExpenseId, cancellationToken);
        Assert.Equal("Settled", payment.Status);
        Assert.Equal(2_300m, payment.ActualAmount);
        Assert.True(payment.WithdrawalCreated);

        var manual = Assert.Single(await WithdrawalsAsync(client, company.MoneyAssetId, cancellationToken));
        var deleted = await client.DeleteAsync(TransactionUri(company.MoneyAssetId, manual.Id), cancellationToken);
        Assert.True(deleted.IsSuccessStatusCode, $"delete returned {(int)deleted.StatusCode}.");
        var recreated = await client.PutAsJsonAsync(
            ExpensePaymentSettlementUri(settled.ExpenseId, October),
            SettleExpensePaymentBody(company.AccountId, 2_300m, newDate, createWithdrawal: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, recreated.StatusCode);
        var created = Assert.Single(await WithdrawalsAsync(client, company.MoneyAssetId, cancellationToken));
        Assert.NotEqual(manual.Id, created.Id);
        Assert.Equal(2_300m, created.Quantity);
    }

    [Fact]
    public async Task Undo_RemovesRowAndWithdrawal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settled = await client.CreateExpensePaymentWithdrawalAsync(cancellationToken);
        Assert.Equal(2_650m, await client.GetCashBalanceAsync(settled.Account.AccountId, cancellationToken));

        var response = await client.DeleteAsync(ExpensePaymentSettlementUri(settled.ExpenseId, October), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var payment = await OctoberPaymentAsync(client, settled.ExpenseId, cancellationToken);
        Assert.Equal("Pending", payment.Status);
        Assert.Null(payment.ActualAmount);
        Assert.Null(payment.ActualDate);
        Assert.Null(payment.Difference);
        Assert.False(payment.WithdrawalCreated);
        Assert.Empty(await WithdrawalsAsync(client, settled.Account.MoneyAssetId, cancellationToken));
        Assert.Equal(5_000m, await client.GetCashBalanceAsync(settled.Account.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Undo_AfterManualDelete_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settled = await client.CreateExpensePaymentWithdrawalAsync(cancellationToken);
        var deleted = await client.DeleteAsync(TransactionUri(settled.Account.MoneyAssetId, settled.TransactionId), cancellationToken);
        Assert.True(deleted.IsSuccessStatusCode, $"delete returned {(int)deleted.StatusCode}.");

        var response = await client.DeleteAsync(ExpensePaymentSettlementUri(settled.ExpenseId, October), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var payment = await OctoberPaymentAsync(client, settled.ExpenseId, cancellationToken);
        Assert.Equal("Pending", payment.Status);
        Assert.Equal(5_000m, await client.GetCashBalanceAsync(settled.Account.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Undo_NotSettled_Returns404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var expense = await client.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 2_400m, dayOfMonth: 5);

        var response = await client.DeleteAsync(ExpensePaymentSettlementUri(expense, October), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.NotFound, ExpensePaymentNotFoundErrorCode, cancellationToken);
        Assert.Equal("Pending", (await OctoberPaymentAsync(client, expense, cancellationToken)).Status);
    }

    [Fact]
    public async Task ManualWithdrawalEdit_KeepsSettlement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settled = await client.CreateExpensePaymentWithdrawalAsync(cancellationToken);
        var update = new UpdateTransactionRequest
        {
            Type = TransactionType.Withdraw,
            Quantity = 2_400m,
            UnitPrice = 1m,
            Date = ExpenseSettlementDay
        };

        var response = await client.PutAsJsonAsync(
            TransactionUri(settled.Account.MoneyAssetId, settled.TransactionId), update, cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"edit returned {(int)response.StatusCode}.");
        var payment = await OctoberPaymentAsync(client, settled.ExpenseId, cancellationToken);
        Assert.Equal("Settled", payment.Status);
        Assert.Equal(2_350m, payment.ActualAmount);
        Assert.True(payment.WithdrawalCreated);
    }

    [Fact]
    public async Task Settle_RememberAccount_WritesVersionFromMonth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var expense = await client.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 1_000m, dayOfMonth: 5);
        await client.UpdateRecurringExpenseAsync(expense, "2026-09", cancellationToken, "Czynsz", 1_100m, dayOfMonth: 5);
        await client.UpdateRecurringExpenseAsync(expense, "2026-10", cancellationToken, "Czynsz", 1_200m, dayOfMonth: 5);
        var versionsBefore = await CountRecurringExpenseVersionsAsync(userId, expense, cancellationToken);

        var response = await client.PutAsJsonAsync(
            ExpensePaymentSettlementUri(expense, "2026-11"),
            SettleExpensePaymentBody(account.AccountId, 1_200m, new DateOnly(2026, 11, 10), rememberAccount: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(versionsBefore + 1, await CountRecurringExpenseVersionsAsync(userId, expense, cancellationToken));
        Assert.Null((await client.ExpenseDefinitionAsync(expense, "2026-09", cancellationToken)).AccountId);
        Assert.Null((await client.ExpenseDefinitionAsync(expense, "2026-10", cancellationToken)).AccountId);
        var november = await client.ExpenseDefinitionAsync(expense, "2026-11", cancellationToken);
        Assert.Equal((account.AccountId, "mBank ROR"), (november.AccountId, november.AccountName));
    }

    [Fact]
    public async Task Settle_NoRemember_LeavesVersions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var expense = await client.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 1_000m, dayOfMonth: 5);
        await client.UpdateRecurringExpenseAsync(expense, "2026-09", cancellationToken, "Czynsz", 1_100m, dayOfMonth: 5);
        await client.UpdateRecurringExpenseAsync(expense, "2026-10", cancellationToken, "Czynsz", 1_200m, dayOfMonth: 5);
        var versionsBefore = await CountRecurringExpenseVersionsAsync(userId, expense, cancellationToken);

        var response = await client.PutAsJsonAsync(
            ExpensePaymentSettlementUri(expense, "2026-11"),
            SettleExpensePaymentBody(account.AccountId, 1_200m, new DateOnly(2026, 11, 10), rememberAccount: false),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(versionsBefore, await CountRecurringExpenseVersionsAsync(userId, expense, cancellationToken));
        Assert.Null((await client.ExpenseDefinitionAsync(expense, "2026-11", cancellationToken)).AccountId);
    }
}
