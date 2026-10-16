using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// Recurring expenses have no GET by id, so the isolation facts are written by hand instead of through the TenancyIsolationTests template.
public sealed class RecurringExpenseTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task EndAndDelete_ByStranger_ReturnNotFoundAndLeaveExpenseUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var expense = await owner.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 2_000m, dayOfMonth: 1);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var end = await stranger.PostAsJsonAsync(
            EndRecurringExpenseUri(expense), new { endDate = new DateOnly(2026, 12, 31) }, cancellationToken);
        var delete = await stranger.DeleteAsync(RecurringExpenseUri(expense), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, end.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        var definition = Assert.Single((await owner.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.Definitions);
        Assert.Null(definition.EndDate);
        Assert.Equal(2_000m, definition.Amount);
    }

    [Fact]
    public async Task UpdateAndDeleteChange_ByStranger_ReturnNotFoundAndLeaveExpenseUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var oc = await owner.CreateOcToyotaAsync(cancellationToken);
        await owner.UpdateRecurringExpenseAsync(
            oc, "2027-01", cancellationToken, "OC Toyota", 1_320m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 15);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var update = await stranger.PutAsJsonAsync(
            RecurringExpenseUri(oc),
            UpdateRecurringExpenseBody("2027-01", "Stolen", 1_000m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 15),
            cancellationToken);
        var deleteChange = await stranger.DeleteAsync(RecurringExpenseChangeUri(oc, "2027-01"), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteChange.StatusCode);
        var definition = await owner.ExpenseDefinitionAsync(oc, "2026-10", cancellationToken);
        Assert.Equal("OC Toyota", definition.Name);
        Assert.Equal(1_200m, definition.Amount);
        Assert.NotNull(definition.NextChange);
        Assert.Equal(1_320m, definition.NextChange.Amount);
    }

    [Fact]
    public async Task Month_ForStranger_DoesNotShowOrCountOwnersExpenses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await owner.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 2_000m, dayOfMonth: 1);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var month = await stranger.GetPlanningMonthAsync("2026-10", cancellationToken);

        Assert.Empty(month.Expenses.Payments);
        Assert.Empty(month.Expenses.Definitions);
        Assert.Equal(0m, month.Expenses.CostPln);
        Assert.Equal(0m, month.FreeFunds.PlanPln);
        Assert.Equal(0m, month.FreeFunds.ForecastPln);
    }
}
