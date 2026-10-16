using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningMonthFreeFundsTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task PlanAndForecast()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var incomes = await client.CreateHouseholdIncomesAsync(cancellationToken);
        await client.CreateLivingExpensesAsync(cancellationToken);
        await client.CreateOcToyotaAsync(cancellationToken);
        Factory.Clock.SetUtcNow(AfterDueDayUtc);

        var initial = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).FreeFunds;

        Assert.Equal(new FreeFundsView(12_300m, 12_300m, false), initial);

        await client.SettleOccurrenceAsync(
            incomes.Dominik, "2026-10", incomes.Account.AccountId, 9_143.27m, new DateOnly(2026, 10, 30), cancellationToken);
        await client.SettleOccurrenceAsync(
            incomes.Zona, "2026-10", incomes.Account.AccountId, 6_500m, new DateOnly(2026, 10, 9), cancellationToken);
        var settled = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).FreeFunds;

        Assert.Equal(new FreeFundsView(12_300m, 12_243.27m, false), settled);

        await client.SkipOccurrenceAsync(incomes.Plus800, "2026-10", cancellationToken);
        var skipped = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).FreeFunds;

        Assert.Equal(12_300m, skipped.PlanPln);
        Assert.Equal(11_443.27m, skipped.ForecastPln);
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
