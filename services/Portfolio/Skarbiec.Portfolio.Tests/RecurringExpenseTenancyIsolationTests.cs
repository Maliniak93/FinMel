using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// Recurring expenses have no GET or PUT by id, so the isolation facts are written by hand instead of through the TenancyIsolationTests template.
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
