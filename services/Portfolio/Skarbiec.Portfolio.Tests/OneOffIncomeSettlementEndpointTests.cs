using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The settle and undo slices are called directly and asserted on the raw response; PlanningApi helpers only arrange and read back.
public sealed class OneOffIncomeSettlementEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static readonly DateOnly PlannedDate = new(2026, 10, 10);

    [Fact]
    public async Task Settle_MovesPlanToActual()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var exact = await client.CreateOneOffIncomeAsync(account.AccountId, cancellationToken, "Premia", 2_500m, PlannedDate);
        var other = await client.CreateOneOffIncomeAsync(account.AccountId, cancellationToken, "Zwrot", 1_000m, new DateOnly(2026, 10, 25));
        var before = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes;
        Assert.Equal(3_500m, before.Balance.PendingPln);
        Assert.Equal("Pending", before.OneOff(exact).Status);

        var response = await client.PutAsJsonAsync(
            OneOffIncomeSettlementUri(exact),
            SettleOccurrenceBody(account.AccountId, 2_500m, new DateOnly(2026, 10, 12)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var settledMonth = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes;
        var row = settledMonth.OneOff(exact);
        Assert.Equal("Settled", row.Status);
        Assert.Equal(2_500m, row.Amount);
        Assert.Equal(PlannedDate, row.Date);
        Assert.Equal(2_500m, row.ActualAmount);
        Assert.Equal(new DateOnly(2026, 10, 12), row.ActualDate);
        Assert.Equal(2_500m, row.ActualAmountPln);
        Assert.Equal(0m, row.Difference);
        Assert.False(row.DepositCreated);
        Assert.False(row.Overdue);
        Assert.Equal(2_500m, settledMonth.Balance.ActualPln);
        Assert.Equal(1_000m, settledMonth.Balance.PendingPln);
        Assert.Equal(0m, settledMonth.Balance.DifferencePln);
        Assert.Equal("Pending", settledMonth.OneOff(other).Status);
        Assert.Empty((await client.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items);

        var different = await client.PutAsJsonAsync(
            OneOffIncomeSettlementUri(exact),
            SettleOccurrenceBody(account.AccountId, 2_400m, new DateOnly(2026, 10, 12)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, different.StatusCode);
        var differentMonth = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes;
        Assert.Equal(-100m, differentMonth.OneOff(exact).Difference);
        Assert.Equal(2_400m, differentMonth.Balance.ActualPln);
        Assert.Equal(-100m, differentMonth.Balance.DifferencePln);
    }

    [Fact]
    public async Task Settle_InvalidRequests()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var pln = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var eur = await client.CreateBankAccountAsync(cancellationToken, "Revolut EUR", currency: "EUR");
        var archived = await client.CreateBankAccountAsync(cancellationToken, "Stare konto");
        await client.ArchiveAccountAsync(archived.AccountId, cancellationToken);
        var income = await client.CreateOneOffIncomeAsync(pln.AccountId, cancellationToken, "Premia", 500m, PlannedDate);

        var future = await client.PutAsJsonAsync(
            OneOffIncomeSettlementUri(income),
            SettleOccurrenceBody(pln.AccountId, 500m, new DateOnly(2026, 10, 21)),
            cancellationToken);
        var currency = await client.PutAsJsonAsync(
            OneOffIncomeSettlementUri(income),
            SettleOccurrenceBody(eur.AccountId, 500m, PlannedDate),
            cancellationToken);
        var archivedAccount = await client.PutAsJsonAsync(
            OneOffIncomeSettlementUri(income),
            SettleOccurrenceBody(archived.AccountId, 500m, PlannedDate),
            cancellationToken);
        var unknown = await client.PutAsJsonAsync(
            OneOffIncomeSettlementUri(Guid.NewGuid()),
            SettleOccurrenceBody(pln.AccountId, 500m, PlannedDate),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        await currency.AssertProblemAsync(HttpStatusCode.BadRequest, CurrencyMismatchErrorCode, cancellationToken);
        await archivedAccount.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountArchivedErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var row = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.OneOff(income);
        Assert.Equal("Pending", row.Status);
        Assert.Null(row.ActualAmount);
    }

    [Fact]
    public async Task Undo_ReturnsToPending()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.CreateOneOffIncomeDepositAsync(cancellationToken);
        Assert.Equal(2_500m, await client.GetCashBalanceAsync(deposit.Account.AccountId, cancellationToken));

        var response = await client.DeleteAsync(OneOffIncomeSettlementUri(deposit.IncomeId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = (await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes.OneOff(deposit.IncomeId);
        Assert.Equal("Pending", row.Status);
        Assert.Null(row.ActualAmount);
        Assert.Null(row.ActualDate);
        Assert.Null(row.Difference);
        Assert.False(row.DepositCreated);
        Assert.Empty((await client.ListTransactionsAsync(deposit.Account.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal(0m, await client.GetCashBalanceAsync(deposit.Account.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Undo_WhenPending_Returns404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        var income = await client.CreateOneOffIncomeAsync(account.AccountId, cancellationToken, "Premia", 500m, PlannedDate);

        var response = await client.DeleteAsync(OneOffIncomeSettlementUri(income), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Pending", (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.OneOff(income).Status);
    }
}
