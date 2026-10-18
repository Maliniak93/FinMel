using System.Net;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The year is read through PlanningApi.GetPlanningYearAsync, which only asserts success; the 400 fact calls the route raw.
public sealed class PlanningYearEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static readonly DateTimeOffset MarchCreationUtc = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MatchesMonthReads()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await ArrangeHouseholdYearAsync(client, cancellationToken);

        var year = await client.GetPlanningYearAsync(2026, cancellationToken);

        Assert.Equal(12, year.Months.Count);
        for (var index = 0; index < 12; index++)
        {
            var key = $"2026-{index + 1:00}";
            var row = year.Months[index];
            var month = await client.GetPlanningMonthAsync(key, cancellationToken);

            Assert.Equal(key, row.Month);
            Assert.True(row.HasDefinitions, key);
            Assert.Equal(month.Incomes.Balance.PlanPln, row.IncomePlanPln);
            Assert.Equal(month.Incomes.Balance.DifferencePln, row.DifferencePln);
            Assert.Equal(month.Expenses.CostPln, row.CostPln);
            Assert.Equal(month.FreeFunds.PlanPln, row.FreeFundsPlanPln);
            Assert.Equal(month.FreeFunds.ForecastPln, row.FreeFundsForecastPln);
            Assert.Equal(month.Allocation.EffectiveFrom is null ? (decimal?)null : month.Allocation.ToInvestPln, row.ToInvestPln);
            Assert.Equal(month.TransferPlan!.Status.GetRawText(), row.TransferPlanStatus?.GetRawText());
        }
    }

    [Fact]
    public async Task ActualAndEmptyMonths()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MarchCreationUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var salary = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Pensja", 5_000m, new DateOnly(2026, 3, 1), dayOfMonth: 10);
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        await client.SkipOccurrenceAsync(salary, "2026-05", cancellationToken);

        var year = await client.GetPlanningYearAsync(2026, cancellationToken);

        foreach (var key in new[] { "2026-01", "2026-02" })
        {
            var empty = year.Months.Single(m => m.Month == key);
            Assert.False(empty.HasDefinitions, key);
            Assert.False(empty.HasOverdue, key);
            Assert.All(
                new[]
                {
                    empty.IncomePlanPln, empty.IncomeActualPln, empty.DifferencePln, empty.CostPln,
                    empty.FreeFundsPlanPln, empty.FreeFundsForecastPln, empty.ToInvestPln
                },
                value => Assert.Null(value));
            Assert.Null(empty.TransferPlanStatus);
        }

        var march = year.Months.Single(m => m.Month == "2026-03");
        var april = year.Months.Single(m => m.Month == "2026-04");
        var may = year.Months.Single(m => m.Month == "2026-05");
        Assert.True(march.HasOverdue);
        Assert.True(april.HasOverdue);
        Assert.Equal(0m, april.IncomeActualPln);
        Assert.Equal(0m, may.IncomeActualPln);
        Assert.False(may.HasOverdue);
        Assert.All(
            year.Months.Where(m => m.Month is "2026-11" or "2026-12"),
            m => Assert.Null(m.IncomeActualPln));
    }

    [Fact]
    public async Task Totals()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await ArrangeHouseholdYearAsync(client, cancellationToken);

        var year = await client.GetPlanningYearAsync(2026, cancellationToken);

        Assert.Equal(ColumnSum(year, m => m.IncomePlanPln), year.Totals.IncomePlanPln);
        Assert.Equal(ColumnSum(year, m => m.IncomeActualPln), year.Totals.IncomeActualPln);
        Assert.Equal(ColumnSum(year, m => m.DifferencePln), year.Totals.DifferencePln);
        Assert.Equal(ColumnSum(year, m => m.CostPln), year.Totals.CostPln);
        Assert.Equal(ColumnSum(year, m => m.FreeFundsPlanPln), year.Totals.FreeFundsPlanPln);
        Assert.Equal(ColumnSum(year, m => m.FreeFundsForecastPln), year.Totals.FreeFundsForecastPln);
        Assert.Equal(ColumnSum(year, m => m.ToInvestPln), year.Totals.ToInvestPln);
    }

    [Fact]
    public async Task Range()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Pensja", 5_000m, new DateOnly(2026, 3, 1), dayOfMonth: 10);

        var year = await client.GetPlanningYearAsync(2026, cancellationToken);

        Assert.Equal((2026, 2027), (year.FirstYear, year.LastYear));
        using var tooEarly = await client.GetAsync(PlanningYearUri(2025), cancellationToken);
        await tooEarly.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidYearErrorCode, cancellationToken);
        using var tooLate = await client.GetAsync(PlanningYearUri(2028), cancellationToken);
        await tooLate.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidYearErrorCode, cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var withoutDefinitions = await stranger.GetPlanningYearAsync(2026, cancellationToken);

        Assert.Equal((2026, 2027), (withoutDefinitions.FirstYear, withoutDefinitions.LastYear));
    }

    [Fact]
    public async Task SingleRateLookup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithRate("EUR", 4.25m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var eur = await client.CreateBankAccountAsync(cancellationToken, "Revolut EUR", currency: "EUR");
        await client.CreateRecurringIncomeAsync(eur.AccountId, cancellationToken, "Najem EUR", 1_000m, dayOfMonth: 15);
        await client.CreateRecurringIncomeAsync(eur.AccountId, cancellationToken, "Czynsz EUR", 500m, dayOfMonth: 20);
        var callsBefore = Factory.InstrumentQuoteLookupClient.RateCalls.Count;

        await client.GetPlanningYearAsync(2026, cancellationToken);

        var call = Assert.Single(Factory.InstrumentQuoteLookupClient.RateCalls.Skip(callsBefore));
        Assert.Contains("EUR", call);
    }

    private static async Task ArrangeHouseholdYearAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var incomes = await client.CreateHouseholdIncomesAsync(cancellationToken);
        await client.CreateLivingExpensesAsync(cancellationToken);
        await client.SettleOccurrenceAsync(
            incomes.Dominik, "2026-09", incomes.Account.AccountId, 9_143.27m, SeptemberLastDay, cancellationToken);
        await client.SettleOccurrenceAsync(
            incomes.Zona, "2026-10", incomes.Account.AccountId, 6_400m, new DateOnly(2026, 10, 9), cancellationToken);
        await client.SkipOccurrenceAsync(incomes.Plus800, "2026-10", cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, FirstAllocationPlan());
    }

    private static decimal ColumnSum(PlanningYearView year, Func<PlanningYearMonthView, decimal?> column) =>
        year.Months.Sum(m => column(m) ?? 0m);
}
