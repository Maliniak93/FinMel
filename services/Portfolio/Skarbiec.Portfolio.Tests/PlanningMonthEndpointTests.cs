using System.Net;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// Incomes are arranged through PlanningApi; the month read goes through its helper, which only asserts success, and the PLN-only fact calls it raw.
public sealed class PlanningMonthEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task ReturnsOccurrencesAndTotal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Wypłata Dominik", 9_200m, new DateOnly(2026, 1, 1),
            dayKind: RecurrenceDayKind.LastBusinessDay, dayOfMonth: null);
        await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Wypłata żona", 6_500m, new DateOnly(2026, 1, 1),
            dayOfMonth: 10, shift: BusinessDayShift.PreviousBusinessDay);
        await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "800+", 800m, new DateOnly(2024, 6, 10), new DateOnly(2032, 3, 15), dayOfMonth: 10);

        var october = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes;
        var november = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes;

        Assert.Equal(
            [(new DateOnly(2026, 10, 9), 6_500m), (new DateOnly(2026, 10, 10), 800m), (new DateOnly(2026, 10, 30), 9_200m)],
            october.Occurrences.Select(o => (o.Date, o.Amount)).ToArray());
        Assert.Equal(["Wypłata żona", "800+", "Wypłata Dominik"], october.Occurrences.Select(o => o.Name).ToArray());
        Assert.All(october.Occurrences, o =>
        {
            Assert.Equal("2026-10", o.NominalMonth);
            Assert.Equal("mBank ROR", o.AccountName);
            Assert.Equal(account.AccountId, o.AccountId);
            Assert.False(o.AccountArchived);
            Assert.Equal("PLN", o.Currency);
            Assert.Equal(o.Amount, o.AmountPln);
            Assert.False(o.Estimated);
        });
        Assert.Equal(16_500m, october.TotalPln);
        Assert.False(october.FxUnavailable);
        Assert.Equal(
            [new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 30)],
            november.Occurrences.Select(o => o.Date).ToArray());
        Assert.Equal(["800+", "Wypłata żona", "Wypłata Dominik"], november.Occurrences.Select(o => o.Name).ToArray());
    }

    [Fact]
    public async Task ShiftedOccurrence_BelongsToItsShiftedMonth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Zwrot kosztów", 300m, new DateOnly(2026, 7, 1),
            dayOfMonth: 1, shift: BusinessDayShift.PreviousBusinessDay);

        var july = (await client.GetPlanningMonthAsync("2026-07", cancellationToken)).Incomes;
        var august = (await client.GetPlanningMonthAsync("2026-08", cancellationToken)).Incomes;

        Assert.Equal(
            [(new DateOnly(2026, 7, 1), "2026-07"), (new DateOnly(2026, 7, 31), "2026-08")],
            july.Occurrences.Select(o => (o.Date, o.NominalMonth)).ToArray());
        Assert.Empty(august.Occurrences);
    }

    [Fact]
    public async Task ForeignIncome_UsesLatestRate_FailsSoft()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithRate("EUR", 4.25m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var pln = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var eur = await client.CreateBankAccountAsync(cancellationToken, "Revolut EUR", currency: "EUR");
        await client.CreateRecurringIncomeAsync(pln.AccountId, cancellationToken, "Pensja", 500m, dayOfMonth: 5);
        await client.CreateRecurringIncomeAsync(eur.AccountId, cancellationToken, "Najem EUR", 1_000m, dayOfMonth: 15);

        var known = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes;

        var rent = Assert.Single(known.Occurrences, o => o.Name == "Najem EUR");
        Assert.Equal((1_000m, "EUR", 4_250m, true), (rent.Amount, rent.Currency, rent.AmountPln, rent.Estimated));
        Assert.Equal(4_750m, known.TotalPln);
        Assert.False(known.FxUnavailable);

        Factory.InstrumentQuoteLookupClient.WithUnavailable();
        var unavailable = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes;

        var unknownRent = Assert.Single(unavailable.Occurrences, o => o.Name == "Najem EUR");
        Assert.Null(unknownRent.AmountPln);
        Assert.True(unknownRent.Estimated);
        Assert.Equal(1_000m, unknownRent.Amount);
        Assert.Equal(500m, unavailable.TotalPln);
        Assert.True(unavailable.FxUnavailable);
    }

    [Fact]
    public async Task PlnOnly_DoesNotCallFx()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        await client.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "Pensja", 500m);

        var response = await client.GetAsync(PlanningMonthUri("2026-10"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Factory.InstrumentQuoteLookupClient.RateCalls);
    }

    [Fact]
    public async Task Definitions_CarryStatusAndValuesInForce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Premia roczna", 12_000m, new DateOnly(2027, 3, 1),
            intervalMonths: 12, dayOfMonth: 1, shift: BusinessDayShift.NextBusinessDay);
        await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "800+", 800m, new DateOnly(2024, 6, 10), new DateOnly(2032, 3, 15), dayOfMonth: 10);

        var october = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Definitions;
        var april = (await client.GetPlanningMonthAsync("2032-04", cancellationToken)).Incomes.Definitions;

        Assert.Equal(["800+", "Premia roczna"], october.Select(d => d.Name).ToArray());
        Assert.Equal("Active", october[0].Status);
        Assert.Equal("Upcoming", october[1].Status);
        Assert.Equal(["800+", "Premia roczna"], april.Select(d => d.Name).ToArray());
        Assert.Equal("Ended", april[0].Status);
        Assert.Equal(new DateOnly(2032, 3, 15), april[0].EndDate);

        var premia = october[1];
        Assert.Equal(new DateOnly(2027, 3, 1), premia.StartDate);
        Assert.Null(premia.EndDate);
        Assert.Equal((12_000m, "PLN", account.AccountId, "mBank ROR", false), (premia.Amount, premia.Currency, premia.AccountId, premia.AccountName, premia.AccountArchived));
        Assert.Equal((12, 1), (premia.IntervalMonths, premia.DayOfMonth));
        Assert.True(premia.DayKind.Is(RecurrenceDayKind.DayOfMonth));
        Assert.True(premia.Shift.Is(BusinessDayShift.NextBusinessDay));

        var benefit = april[0];
        Assert.Equal((800m, "PLN", account.AccountId), (benefit.Amount, benefit.Currency, benefit.AccountId));
        Assert.Equal((1, 10), (benefit.IntervalMonths, benefit.DayOfMonth));
        Assert.True(benefit.Shift.Is(BusinessDayShift.None));
    }
}
