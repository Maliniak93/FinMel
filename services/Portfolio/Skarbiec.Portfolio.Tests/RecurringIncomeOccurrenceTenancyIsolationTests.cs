using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// Occurrences are nested under a recurring income with no GET by id, so the isolation facts are written by hand instead of through the TenancyIsolationTests template.
public sealed class RecurringIncomeOccurrenceTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task SettleSkipAndUndo_ByStranger_ReturnNotFoundAndLeaveOccurrencesUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await owner.CreateBankAccountAsync(cancellationToken);
        var income = await owner.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "Pensja", 500m, dayOfMonth: 10);
        await owner.SettleOccurrenceAsync(income, "2026-10", account.AccountId, 480m, new DateOnly(2026, 10, 10), cancellationToken);
        await owner.SkipOccurrenceAsync(income, "2026-09", cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangersAccount = await stranger.CreateBankAccountAsync(cancellationToken, "Moje konto");
        var strangersIncome = await stranger.CreateRecurringIncomeAsync(strangersAccount.AccountId, cancellationToken, "Własny wpływ", 100m, dayOfMonth: 10);
        var settle = await stranger.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-11"),
            SettleOccurrenceBody(strangersAccount.AccountId, 500m, new DateOnly(2026, 10, 10)),
            cancellationToken);
        var settleStored = await stranger.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-10"),
            SettleOccurrenceBody(strangersAccount.AccountId, 1m, new DateOnly(2026, 10, 11)),
            cancellationToken);
        var skip = await stranger.PutAsync(SkipOccurrenceUri(income, "2026-11"), content: null, cancellationToken);
        var undoSettled = await stranger.DeleteAsync(OccurrenceUri(income, "2026-10"), cancellationToken);
        var undoSkipped = await stranger.DeleteAsync(OccurrenceUri(income, "2026-09"), cancellationToken);
        var ontoOwnersAccount = await stranger.PutAsJsonAsync(
            SettleOccurrenceUri(strangersIncome, "2026-10"),
            SettleOccurrenceBody(account.AccountId, 100m, new DateOnly(2026, 10, 10)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, settle.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, settleStored.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, skip.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, undoSettled.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, undoSkipped.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ontoOwnersAccount.StatusCode);
        var october = (await owner.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Occurrence("2026-10");
        Assert.Equal("Settled", october.Status);
        Assert.Equal(480m, october.ActualAmount);
        Assert.Equal("Skipped", (await owner.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes.Occurrence("2026-09").Status);
        Assert.Equal("Pending", (await owner.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes.Occurrence("2026-11").Status);
        Assert.Equal("Pending", (await stranger.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Occurrence("2026-10").Status);
    }

    [Fact]
    public async Task Month_ForStranger_DoesNotShowOwnersStoredOccurrences()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await owner.CreateBankAccountAsync(cancellationToken);
        var income = await owner.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "Pensja", 500m, dayOfMonth: 10);
        await owner.SettleOccurrenceAsync(income, "2026-10", account.AccountId, 480m, new DateOnly(2026, 10, 10), cancellationToken);
        await owner.SkipOccurrenceAsync(income, "2026-09", cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var october = (await stranger.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes;
        var september = (await stranger.GetPlanningMonthAsync("2026-09", cancellationToken)).Incomes;

        Assert.Empty(october.Occurrences);
        Assert.Empty(september.Occurrences);
        Assert.Equal(0m, october.TotalPln);
    }

    [Fact]
    public async Task Settle_WithDepositOntoOwnersAccount_ReturnsNotFoundAndLeavesBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await owner.CreateBankAccountAsync(cancellationToken);
        var income = await owner.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "Pensja", 500m, dayOfMonth: 10);
        await owner.SettleOccurrenceAsync(income, "2026-10", account.AccountId, 480m, new DateOnly(2026, 10, 10), cancellationToken, createDeposit: true);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangersAccount = await stranger.CreateBankAccountAsync(cancellationToken, "Moje konto");
        var strangersIncome = await stranger.CreateRecurringIncomeAsync(strangersAccount.AccountId, cancellationToken, "Własny wpływ", 100m, dayOfMonth: 10);
        var ontoOwnersAccount = await stranger.PutAsJsonAsync(
            SettleOccurrenceUri(strangersIncome, "2026-10"),
            SettleOccurrenceBody(account.AccountId, 100m, new DateOnly(2026, 10, 10), createDeposit: true),
            cancellationToken);
        var ownersOccurrence = await stranger.PutAsJsonAsync(
            SettleOccurrenceUri(income, "2026-10"),
            SettleOccurrenceBody(strangersAccount.AccountId, 1m, new DateOnly(2026, 10, 11), createDeposit: true),
            cancellationToken);
        var undo = await stranger.DeleteAsync(OccurrenceUri(income, "2026-10"), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, ontoOwnersAccount.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ownersOccurrence.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, undo.StatusCode);
        Assert.Equal(480m, await owner.GetCashBalanceAsync(account.AccountId, cancellationToken));
        Assert.Single((await owner.ListTransactionsAsync(account.MoneyAssetId, cancellationToken)).Items);
        Assert.Empty((await stranger.ListTransactionsAsync(strangersAccount.MoneyAssetId, cancellationToken)).Items);
        Assert.Equal("Pending", (await stranger.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Occurrence("2026-10").Status);
    }
}
