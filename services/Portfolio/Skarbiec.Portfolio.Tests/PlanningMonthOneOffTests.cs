using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningMonthOneOffTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Income_CountsInItsMonthOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        await client.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "Wypłata", 6_000m, dayOfMonth: 10);
        await client.CreateOneOffIncomeAsync(account.AccountId, cancellationToken, "Premia", 8_000m, new DateOnly(2026, 12, 15));

        var november = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes.Balance;
        var december = (await client.GetPlanningMonthAsync("2026-12", cancellationToken)).Incomes.Balance;

        Assert.Equal((6_000m, 6_000m), (november.PlanPln, november.PendingPln));
        Assert.Equal((14_000m, 14_000m), (december.PlanPln, december.PendingPln));
    }

    [Fact]
    public async Task Expense_CostsOnlyItsMonth_NoReserve()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateCushionSetupAsync(cancellationToken);
        await client.CreateOneOffExpenseAsync(cancellationToken, "Wakacje", 1_200m, new DateOnly(2026, 11, 10));

        var october = await client.GetPlanningMonthAsync("2026-10", cancellationToken);
        var november = await client.GetPlanningMonthAsync("2026-11", cancellationToken);
        var december = await client.GetPlanningMonthAsync("2026-12", cancellationToken);

        Assert.Equal(4_200m, october.Expenses.CostPln);
        Assert.Equal(5_400m, november.Expenses.CostPln);
        Assert.Equal(4_200m, december.Expenses.CostPln);
        Assert.Equal(october.FreeFunds.PlanPln - 1_200m, november.FreeFunds.PlanPln);
        Assert.Equal(october.FreeFunds.PlanPln, december.FreeFunds.PlanPln);
        Assert.Equal(october.FreeFunds.ForecastPln - 1_200m, november.FreeFunds.ForecastPln);
    }

    [Fact]
    public async Task ForeignIncome_UsesLatestRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        Factory.InstrumentQuoteLookupClient.WithRate("EUR", 4.25m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var eur = await client.CreateBankAccountAsync(cancellationToken, "Revolut EUR", currency: "EUR");
        await client.CreateOneOffIncomeAsync(eur.AccountId, cancellationToken, "Zwrot EUR", 1_000m, new DateOnly(2026, 11, 15));

        var known = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes;

        var row = Assert.Single(known.OneOffs!);
        Assert.Equal((1_000m, "EUR", 4_250m, true), (row.Amount, row.Currency, row.AmountPln, row.Estimated));
        Assert.Equal(4_250m, known.Balance.PlanPln);
        Assert.False(known.FxUnavailable);

        Factory.InstrumentQuoteLookupClient.WithUnavailable();
        var unavailable = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes;

        var unknown = Assert.Single(unavailable.OneOffs!);
        Assert.Null(unknown.AmountPln);
        Assert.True(unknown.Estimated);
        Assert.Equal(1_000m, unknown.Amount);
        Assert.Equal(0m, unavailable.Balance.PlanPln);
        Assert.True(unavailable.FxUnavailable);
    }
}
