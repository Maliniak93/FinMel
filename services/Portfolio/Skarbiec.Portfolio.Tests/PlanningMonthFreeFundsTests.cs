using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.GroupFundingApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningMonthFreeFundsTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task PlanAndActual()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var salary = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Wypłata", 8_000m, dayOfMonth: 10);
        var rent = await client.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 3_000m, dayOfMonth: 1);
        await client.CreateRecurringExpenseAsync(cancellationToken, "Rachunki", 2_500m, dayOfMonth: 5);
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Emerytura", 1_000m);
        var pension = await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Emerytalne");
        Factory.Clock.SetUtcNow(PlanningTodayUtc);

        var initial = (await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).FreeFunds;

        Assert.Equal(new FreeFundsView(1_500m, 0m, false), initial);

        await client.SettleOccurrenceAsync(
            salary, CurrentMonth, account.AccountId, 8_000m, new DateOnly(2026, 10, 10), cancellationToken);
        await client.SettleExpensePaymentAsync(
            rent, CurrentMonth, account.AccountId, 3_000m, new DateOnly(2026, 10, 1), cancellationToken);
        await client.DepositMoneyAsync(pension.MoneyAssetId, 600m, FundingDay, cancellationToken);
        var json = await (await client.GetAsync(PlanningMonthUri(CurrentMonth), cancellationToken))
            .Content.ReadAsStringAsync(cancellationToken);
        var settled = (await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).FreeFunds;

        Assert.Equal(new FreeFundsView(1_500m, 4_400m, false), settled);
        Assert.DoesNotContain("forecastPln", json);
    }

    [Fact]
    public async Task SettledOneOffExpense_ChangesFreeFunds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        await client.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "Wypłata", 8_000m, dayOfMonth: 10);
        var baseline = (await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).FreeFunds;
        var id = await client.CreateOneOffExpenseAsync(cancellationToken, "Naprawa", 500m, new DateOnly(2026, 10, 12));
        var planned = (await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).FreeFunds;

        await client.SettleOneOffExpenseAsync(id, account.AccountId, 450m, ExpenseSettlementDay, cancellationToken);

        var settled = (await client.GetPlanningMonthAsync(CurrentMonth, cancellationToken)).FreeFunds;
        Assert.Equal(baseline.ActualPln, planned.ActualPln);
        Assert.Equal(planned.PlanPln + 50m, settled.PlanPln);
        Assert.Equal(planned.ActualPln - 450m, settled.ActualPln);
    }

    [Fact]
    public async Task PastMonthHasNoGroupContributions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateFreeFundsOf7000Async(cancellationToken);
        var before = (await client.GetPlanningMonthAsync("2026-09", cancellationToken)).FreeFunds;
        var groupId = await client.CreateFundingGroupAsync(cancellationToken, "Emerytura", 1_000m);
        await client.CreateBankAccountInGroupAsync(cancellationToken, groupId, "Emerytalne", balance: 600m);

        var september = await client.GetPlanningMonthAsync("2026-09", cancellationToken);

        Assert.Equal(before, september.FreeFunds);
        Assert.All(september.Expenses.GroupContributions ?? [], c => Assert.Null(c.FundedPln));
    }

    [Fact]
    public async Task IncomesNotSpread_DeficitIsNegative()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var incomes = await client.CreateHouseholdIncomesAsync(cancellationToken);
        await client.CreateLivingExpensesAsync(cancellationToken);
        await client.CreateOcToyotaAsync(cancellationToken);
        await client.CreateRecurringIncomeAsync(
            incomes.Account.AccountId, cancellationToken, "Premia", 12_000m, new DateOnly(2027, 3, 1),
            intervalMonths: 12, dayOfMonth: 25);
        await client.CreateRecurringExpenseAsync(
            cancellationToken, "Wkład własny", 13_000m, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 30), dayOfMonth: 5);

        var february = (await client.GetPlanningMonthAsync("2027-02", cancellationToken)).FreeFunds;
        var march = (await client.GetPlanningMonthAsync("2027-03", cancellationToken)).FreeFunds;
        var november = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).FreeFunds;

        Assert.Equal(12_000m, march.PlanPln - february.PlanPln);
        Assert.Equal(-700m, november.PlanPln);
    }

    [Fact]
    public async Task IncompleteWhenFxUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithRate("EUR", 4.25m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var eur = await client.CreateBankAccountAsync(cancellationToken, "Revolut EUR", currency: "EUR");
        await client.CreateRecurringIncomeAsync(eur.AccountId, cancellationToken, "Najem EUR", 1_000m, dayOfMonth: 15);
        await client.CreateRecurringExpenseAsync(cancellationToken, "Rachunki", 100m);
        Factory.InstrumentQuoteLookupClient.WithUnavailable();

        var response = await client.GetAsync(PlanningMonthUri("2026-10"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var month = (await response.Content.ReadFromJsonAsync<PlanningMonthView>(cancellationToken))!;
        Assert.True(month.FreeFunds.Incomplete);
    }
}
