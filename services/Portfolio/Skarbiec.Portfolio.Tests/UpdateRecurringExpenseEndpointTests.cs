using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The update slice is called directly and asserted on the raw response; PlanningApi helpers only arrange and read back.
public sealed class UpdateRecurringExpenseEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task RaiseFromMonth_KeepsEarlierReserves()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var id = await client.CreateOcToyotaAsync(cancellationToken);

        var response = await client.PutAsJsonAsync(
            RecurringExpenseUri(id),
            UpdateRecurringExpenseBody("2027-01", "OC Toyota", 1_320m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 15),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        foreach (var month in new[] { "2026-10", "2026-11", "2026-12" })
        {
            Assert.Equal(200m, (await client.ExpenseDefinitionAsync(id, month, cancellationToken)).ReservePln);
        }

        foreach (var month in new[] { "2027-01", "2027-02", "2027-03" })
        {
            Assert.Equal(240m, (await client.ExpenseDefinitionAsync(id, month, cancellationToken)).ReservePln);
        }

        var march = await client.GetPlanningMonthAsync("2027-03", cancellationToken);
        Assert.Equal(1_320m, Assert.Single(march.Expenses.Payments).Amount);
        var next = (await client.ExpenseDefinitionAsync(id, "2026-10", cancellationToken)).NextChange;
        Assert.NotNull(next);
        Assert.Equal("2027-01", next.EffectiveFrom);
        Assert.Equal(1_320m, next.Amount);
        Assert.Equal(2, await CountRecurringExpenseVersionsAsync(userId, id, cancellationToken));
    }

    [Fact]
    public async Task CutBelowReserved_GivesNegativeReserve()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var id = await client.CreateOcToyotaAsync(cancellationToken);
        var months = new[] { "2027-01", "2027-02", "2027-03" };
        var costBefore = new Dictionary<string, decimal>();
        foreach (var month in months)
        {
            costBefore[month] = (await client.GetPlanningMonthAsync(month, cancellationToken)).Expenses.CostPln;
        }

        var response = await client.PutAsJsonAsync(
            RecurringExpenseUri(id),
            UpdateRecurringExpenseBody("2027-01", "OC Toyota", 300m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 15),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        foreach (var month in months)
        {
            var expenses = (await client.GetPlanningMonthAsync(month, cancellationToken)).Expenses;
            Assert.Equal(-100m, (await client.ExpenseDefinitionAsync(id, month, cancellationToken)).ReservePln);
            Assert.Equal(costBefore[month] - 300m, expenses.CostPln);
        }
    }

    [Fact]
    public async Task IntervalChange_KeepsEarlierReserves()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var id = await client.CreateOcToyotaAsync(cancellationToken);

        var response = await client.PutAsJsonAsync(
            RecurringExpenseUri(id),
            UpdateRecurringExpenseBody("2027-01", "OC Toyota", 1_200m, OcToyotaStart, intervalMonths: 6, dayOfMonth: 15),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var months = new[]
        {
            "2026-10", "2026-11", "2026-12", "2027-01", "2027-02", "2027-03",
            "2027-04", "2027-05", "2027-06", "2027-07", "2027-08", "2027-09",
        };
        foreach (var month in months)
        {
            Assert.Equal(200m, (await client.ExpenseDefinitionAsync(id, month, cancellationToken)).ReservePln);
        }

        var september = await client.GetPlanningMonthAsync("2027-09", cancellationToken);
        var payment = Assert.Single(september.Expenses.Payments);
        Assert.Equal((new DateOnly(2027, 9, 15), 1_200m), (payment.Date, payment.Amount));
    }

    [Fact]
    public async Task EditAtExistingOrBeforeFirstVersion_ReplacesVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var id = await client.CreateOcToyotaAsync(cancellationToken);
        await client.UpdateRecurringExpenseAsync(
            id, "2027-01", cancellationToken, "OC Toyota", 1_320m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 15);

        var atExisting = await client.PutAsJsonAsync(
            RecurringExpenseUri(id),
            UpdateRecurringExpenseBody("2027-01", "OC Toyota", 1_400m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 15),
            cancellationToken);
        var beforeFirst = await client.PutAsJsonAsync(
            RecurringExpenseUri(id),
            UpdateRecurringExpenseBody("2026-09", "OC Toyota", 1_100m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 15),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, atExisting.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, beforeFirst.StatusCode);
        Assert.Equal(2, await CountRecurringExpenseVersionsAsync(userId, id, cancellationToken));
        var october = await client.ExpenseDefinitionAsync(id, "2026-10", cancellationToken);
        Assert.Equal(1_100m, october.Amount);
        Assert.NotNull(october.NextChange);
        Assert.Equal(1_400m, october.NextChange.Amount);
        Assert.Equal("2027-01", october.NextChange.EffectiveFrom);
    }

    [Fact]
    public async Task GlobalFields_ApplyToAllMonths()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var id = await client.CreateOcToyotaAsync(cancellationToken);
        await client.UpdateRecurringExpenseAsync(
            id, "2027-01", cancellationToken, "OC Toyota", 1_320m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 15);
        Factory.Clock.SetUtcNow(new DateTimeOffset(2027, 1, 10, 10, 0, 0, TimeSpan.Zero));

        var rename = await client.PutAsJsonAsync(
            RecurringExpenseUri(id),
            UpdateRecurringExpenseBody("2026-11", "OC", 1_200m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 15),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);
        Assert.Equal("OC", (await client.ExpenseDefinitionAsync(id, "2026-10", cancellationToken)).Name);
        Assert.Equal("OC", (await client.ExpenseDefinitionAsync(id, "2027-03", cancellationToken)).Name);
        Assert.Equal(2, await CountRecurringExpenseVersionsAsync(userId, id, cancellationToken));
        await using (var dbContext = CreateDbContext(userId))
        {
            var expense = await dbContext.Set<RecurringExpense>().SingleAsync(e => e.Id == id, cancellationToken);
            Assert.Equal(new DateOnly(2026, 10, 15), expense.CreatedOn);
        }

        Assert.Equal("2026-10", (await client.ExpenseDefinitionAsync(id, "2026-10", cancellationToken)).EffectiveFrom);
        var moved = await client.PutAsJsonAsync(
            RecurringExpenseUri(id),
            UpdateRecurringExpenseBody("2026-11", "OC", 1_200m, new DateOnly(2027, 6, 15), intervalMonths: 12, dayOfMonth: 15),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, moved.StatusCode);
        Assert.Empty((await client.GetPlanningMonthAsync("2027-03", cancellationToken)).Expenses.Payments);
        var june = Assert.Single((await client.GetPlanningMonthAsync("2027-06", cancellationToken)).Expenses.Payments);
        Assert.Equal((new DateOnly(2027, 6, 15), 1_320m), (june.Date, june.Amount));
    }

    [Fact]
    public async Task Validation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var id = await client.CreateOcToyotaAsync(cancellationToken);
        await client.CreateRecurringExpenseAsync(cancellationToken, "Rachunki", 1_000m, new DateOnly(2026, 1, 1), dayOfMonth: 10);

        var duplicate = await client.PutAsJsonAsync(
            RecurringExpenseUri(id),
            UpdateRecurringExpenseBody("2027-01", "Rachunki", 1_320m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 15),
            cancellationToken);

        await duplicate.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DuplicateNameErrorCode, cancellationToken);

        var invalid = new Dictionary<string, object>
        {
            ["amount 0"] = UpdateRecurringExpenseBody("2027-01", "OC Toyota", 0m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 15),
            ["interval 13"] = UpdateRecurringExpenseBody("2027-01", "OC Toyota", 1_320m, OcToyotaStart, intervalMonths: 13, dayOfMonth: 15),
            ["day 32"] = UpdateRecurringExpenseBody("2027-01", "OC Toyota", 1_320m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 32),
            ["end before start"] = UpdateRecurringExpenseBody(
                "2027-01", "OC Toyota", 1_320m, OcToyotaStart, endDate: new DateOnly(2027, 3, 14), intervalMonths: 12, dayOfMonth: 15),
            ["effectiveFrom 2010-12"] = UpdateRecurringExpenseBody("2010-12", "OC Toyota", 1_320m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 15),
        };
        foreach (var (scenario, body) in invalid)
        {
            var response = await client.PutAsJsonAsync(RecurringExpenseUri(id), body, cancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{scenario} returned {(int)response.StatusCode}.");
        }
    }
}
