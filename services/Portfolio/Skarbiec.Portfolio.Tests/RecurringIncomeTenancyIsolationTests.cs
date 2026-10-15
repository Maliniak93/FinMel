using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// Recurring incomes have no GET or PUT by id, so the isolation facts are written by hand instead of through the TenancyIsolationTests template.
public sealed class RecurringIncomeTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task EndAndDelete_ByStranger_ReturnNotFoundAndLeaveIncomeUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await owner.CreateBankAccountAsync(cancellationToken);
        var income = await owner.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "Pensja", 500m);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var end = await stranger.PostAsJsonAsync(
            EndRecurringIncomeUri(income), new { endDate = new DateOnly(2026, 12, 31) }, cancellationToken);
        var delete = await stranger.DeleteAsync(RecurringIncomeUri(income), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, end.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        var definition = Assert.Single((await owner.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Definitions);
        Assert.Null(definition.EndDate);
        Assert.Equal(500m, definition.Amount);
    }

    [Fact]
    public async Task Create_OnStrangersAccount_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await owner.CreateBankAccountAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.PostAsJsonAsync(
            RecurringIncomesUri, NewRecurringIncomeBody(account.AccountId), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty((await owner.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Definitions);
    }

    [Fact]
    public async Task Month_ForStranger_DoesNotShowOwnersIncomes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await owner.CreateBankAccountAsync(cancellationToken);
        await owner.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "Pensja", 500m);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var incomes = (await stranger.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes;

        Assert.Empty(incomes.Occurrences);
        Assert.Empty(incomes.Definitions);
        Assert.Equal(0m, incomes.TotalPln);
    }
}
