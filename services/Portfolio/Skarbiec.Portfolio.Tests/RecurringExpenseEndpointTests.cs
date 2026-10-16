using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The create, end and delete slices are called directly and asserted on the raw response; PlanningApi helpers only arrange and read back.
public sealed class RecurringExpenseEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task InvalidRequests_Return400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var bodies = new Dictionary<string, object>
        {
            ["amount 0"] = NewRecurringExpenseBody(amount: 0m),
            ["interval 13"] = NewRecurringExpenseBody(intervalMonths: 13),
            ["day 32"] = NewRecurringExpenseBody(dayOfMonth: 32),
            ["last business day with a shift"] = new
            {
                name = "Rachunki",
                amount = 1_000m,
                startDate = new DateOnly(2026, 1, 1),
                intervalMonths = 1,
                dayKind = (int)RecurrenceDayKind.LastBusinessDay,
                shift = (int)BusinessDayShift.PreviousBusinessDay
            },
            ["start before 2011"] = NewRecurringExpenseBody(startDate: new DateOnly(2010, 12, 31)),
            ["end before start"] = NewRecurringExpenseBody(
                startDate: new DateOnly(2026, 5, 1), endDate: new DateOnly(2026, 4, 30)),
        };

        foreach (var (scenario, body) in bodies)
        {
            var response = await client.PostAsJsonAsync(RecurringExpensesUri, body, cancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{scenario} returned {(int)response.StatusCode}.");
        }

        var expenses = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses;
        Assert.Empty(expenses.Definitions);
    }

    [Fact]
    public async Task DuplicateName_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        await client.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "800+", 800m);
        var created = await client.PostAsJsonAsync(
            RecurringExpensesUri, NewRecurringExpenseBody("Rachunki", 1_000m), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var duplicate = await client.PostAsJsonAsync(
            RecurringExpensesUri, NewRecurringExpenseBody("Rachunki", 900m), cancellationToken);
        var namedLikeIncome = await client.PostAsJsonAsync(
            RecurringExpensesUri, NewRecurringExpenseBody("800+", 50m), cancellationToken);

        await duplicate.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DuplicateNameErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, namedLikeIncome.StatusCode);
        var definitions = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.Definitions;
        Assert.Equal(["800+", "Rachunki"], definitions.Select(d => d.Name).ToArray());
        Assert.Equal(1_000m, definitions.Single(d => d.Name == "Rachunki").Amount);
    }

    [Fact]
    public async Task End_RemovesReservesOfDroppedPayments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var oc = await client.CreateOcToyotaAsync(cancellationToken);
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero));

        var ended = await client.PostAsJsonAsync(
            EndRecurringExpenseUri(oc), new { endDate = new DateOnly(2026, 12, 31) }, cancellationToken);
        var afterLimit = await client.PostAsJsonAsync(
            EndRecurringExpenseUri(oc), new { endDate = new DateOnly(2100, 1, 1) }, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, afterLimit.StatusCode);
        foreach (var month in new[] { "2026-10", "2026-11", "2026-12", "2027-03" })
        {
            var expenses = (await client.GetPlanningMonthAsync(month, cancellationToken)).Expenses;

            Assert.Empty(expenses.Payments);
            Assert.Equal(0m, expenses.CostPln);
            Assert.Equal(0m, Assert.Single(expenses.Definitions).ReservePln);
        }

        var definition = Assert.Single((await client.GetPlanningMonthAsync("2027-03", cancellationToken)).Expenses.Definitions);
        Assert.Equal(new DateOnly(2026, 12, 31), definition.EndDate);
        Assert.Equal("Ended", definition.Status);

        var replaced = await client.PostAsJsonAsync(
            EndRecurringExpenseUri(oc), new { endDate = new DateOnly(2028, 3, 31) }, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, replaced.StatusCode);
        var march = (await client.GetPlanningMonthAsync("2028-03", cancellationToken)).Expenses;
        var payment = Assert.Single(march.Payments);
        Assert.Equal((new DateOnly(2028, 3, 15), 1_200m), (payment.Date, payment.Amount));
        Assert.True(march.CostPln > 0m);
        var april = (await client.GetPlanningMonthAsync("2028-04", cancellationToken)).Expenses;
        Assert.Empty(april.Payments);
        Assert.Equal(0m, april.CostPln);
    }

    [Fact]
    public async Task Delete_RemovesExpenseAndVersions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var expense = await client.CreateRecurringExpenseAsync(cancellationToken, "Internet", 60m, dayOfMonth: 5);
        await client.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 2_000m, dayOfMonth: 1);
        await using (var before = CreateDbContext(userId))
        {
            Assert.Equal(2, await before.Set<RecurringExpenseVersion>().CountAsync(cancellationToken));
        }

        var response = await client.DeleteAsync(RecurringExpenseUri(expense), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var october = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses;
        Assert.DoesNotContain(october.Payments, p => p.Name == "Internet");
        Assert.Equal(2_000m, october.CostPln);
        Assert.Equal(["Czynsz"], october.Definitions.Select(d => d.Name).ToArray());
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(1, await dbContext.Set<RecurringExpense>().CountAsync(cancellationToken));
        var version = await dbContext.Set<RecurringExpenseVersion>().SingleAsync(cancellationToken);
        Assert.NotEqual(expense, version.RecurringExpenseId);
    }
}
