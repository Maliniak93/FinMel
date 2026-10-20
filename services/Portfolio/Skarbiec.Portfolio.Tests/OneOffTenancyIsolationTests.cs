using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// One-offs have no GET by id, so the isolation facts are written by hand instead of through the TenancyIsolationTests template.
public sealed class OneOffTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task UpdateAndDelete_ByStranger_ReturnNotFoundAndLeaveEntriesUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await owner.CreateBankAccountAsync(cancellationToken);
        var income = await owner.CreateOneOffIncomeAsync(account.AccountId, cancellationToken, "Premia", 500m);
        var expense = await owner.CreateOneOffExpenseAsync(cancellationToken, "Wakacje", 700m);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangersAccount = await stranger.CreateBankAccountAsync(cancellationToken, "Moje konto");
        var updateIncome = await stranger.PutAsJsonAsync(
            OneOffIncomeUri(income), NewOneOffIncomeBody(strangersAccount.AccountId, "Cudze", 1m), cancellationToken);
        var updateExpense = await stranger.PutAsJsonAsync(
            OneOffExpenseUri(expense), NewOneOffExpenseBody("Cudze", 1m), cancellationToken);
        var deleteIncome = await stranger.DeleteAsync(OneOffIncomeUri(income), cancellationToken);
        var deleteExpense = await stranger.DeleteAsync(OneOffExpenseUri(expense), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, updateIncome.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, updateExpense.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteIncome.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteExpense.StatusCode);
        var month = await owner.GetPlanningMonthAsync("2026-11", cancellationToken);
        var ownIncome = Assert.Single(month.Incomes.OneOffs!);
        Assert.Equal(("Premia", 500m), (ownIncome.Name, ownIncome.Amount));
        var ownExpense = Assert.Single(month.Expenses.OneOffs!);
        Assert.Equal(("Wakacje", 700m), (ownExpense.Name, ownExpense.Amount));
    }

    [Fact]
    public async Task Create_OnStrangersAccount_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await owner.CreateBankAccountAsync(cancellationToken);
        var ownersIncome = await owner.CreateOneOffIncomeAsync(account.AccountId, cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangersAccount = await stranger.CreateBankAccountAsync(cancellationToken, "Moje konto");
        var strangersIncome = await stranger.CreateOneOffIncomeAsync(strangersAccount.AccountId, cancellationToken, "Własna");
        var create = await stranger.PostAsJsonAsync(OneOffIncomesUri, NewOneOffIncomeBody(account.AccountId), cancellationToken);
        var pointAtOwnersAccount = await stranger.PutAsJsonAsync(
            OneOffIncomeUri(strangersIncome), NewOneOffIncomeBody(account.AccountId, "Własna"), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, pointAtOwnersAccount.StatusCode);
        Assert.Equal(ownersIncome, Assert.Single((await owner.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes.OneOffs!).Id);
        var own = Assert.Single((await stranger.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes.OneOffs!);
        Assert.Equal(strangersAccount.AccountId, own.AccountId);
    }

    [Fact]
    public async Task Month_ForStranger_DoesNotShowOwnersOneOffs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await owner.CreateBankAccountAsync(cancellationToken);
        await owner.CreateOneOffIncomeAsync(account.AccountId, cancellationToken, "Premia", 500m);
        await owner.CreateOneOffExpenseAsync(cancellationToken, "Wakacje", 700m);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var month = await stranger.GetPlanningMonthAsync("2026-11", cancellationToken);

        Assert.Empty(month.Incomes.OneOffs!);
        Assert.Empty(month.Expenses.OneOffs!);
        Assert.Equal(0m, month.Incomes.Balance.PlanPln);
        Assert.Equal(0m, month.Expenses.CostPln);
    }
}
