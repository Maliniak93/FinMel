using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningMonthExpenseTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task MonthlyExpenses_PaymentsAndCost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateLivingExpensesAsync(cancellationToken);

        foreach (var (month, first, second) in new[]
        {
            ("2026-09", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10)),
            ("2026-10", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10)),
        })
        {
            var expenses = (await client.GetPlanningMonthAsync(month, cancellationToken)).Expenses;

            Assert.Equal(
                [(first, "Życie", 3_000m), (second, "Rachunki", 1_000m)],
                expenses.Payments.Select(p => (p.Date, p.Name, p.Amount)).ToArray());
            Assert.Equal(4_000m, expenses.CostPln);
        }
    }

    [Fact]
    public async Task YearlyExpense_ReserveFromCreatedMonth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateLivingExpensesAsync(cancellationToken);
        await client.CreateOcToyotaAsync(cancellationToken);
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero));

        var september = (await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Expenses;
        var october = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses;
        var march = (await client.GetPlanningMonthAsync("2027-03", cancellationToken)).Expenses;
        var april = (await client.GetPlanningMonthAsync("2027-04", cancellationToken)).Expenses;

        Assert.Equal(4_000m, september.CostPln);
        Assert.Equal(0m, september.Definitions.Single(d => d.Name == "OC Toyota").ReservePln);
        Assert.Equal(4_200m, october.CostPln);
        var oc = october.Definitions.Single(d => d.Name == "OC Toyota");
        Assert.Equal("Upcoming", oc.Status);
        Assert.Equal(200m, oc.ReservePln);
        Assert.Equal(new NextPaymentView(new DateOnly(2027, 3, 15), 1_200m), oc.NextPayment);
        var payment = Assert.Single(march.Payments, p => p.Name == "OC Toyota");
        Assert.Equal((new DateOnly(2027, 3, 15), "OC Toyota", 1_200m), (payment.Date, payment.Name, payment.Amount));
        Assert.Equal(200m, march.Definitions.Single(d => d.Name == "OC Toyota").ReservePln);
        var aprilOc = april.Definitions.Single(d => d.Name == "OC Toyota");
        Assert.Equal(100m, aprilOc.ReservePln);
        Assert.Equal(new DateOnly(2028, 3, 15), aprilOc.NextPayment!.Date);
    }

    [Fact]
    public async Task SettledPayment_UsesActualInCost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var monthly = await client.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 2_400m, dayOfMonth: 5);
        var quarterly = await client.CreateRecurringExpenseAsync(
            cancellationToken, "Ubezpieczenie", 1_200m, new DateOnly(2026, 3, 1), intervalMonths: 3, dayOfMonth: 5);
        var before = await client.GetPlanningMonthAsync("2026-09", cancellationToken);
        var paidOn = new DateOnly(2026, 9, 5);

        await client.SettleExpensePaymentAsync(monthly, "2026-09", account.AccountId, 2_350m, paidOn, cancellationToken);
        await client.SettleExpensePaymentAsync(quarterly, "2026-09", account.AccountId, 1_260m, paidOn, cancellationToken);

        var after = await client.GetPlanningMonthAsync("2026-09", cancellationToken);
        var czynsz = after.Expenses.Payment(monthly, "2026-09");
        Assert.Equal("Settled", czynsz.Status);
        Assert.Equal(2_400m, czynsz.Amount);
        Assert.Equal((2_350m, paidOn, 2_350m, -50m), (czynsz.ActualAmount, czynsz.ActualDate, czynsz.ActualAmountPln, czynsz.Difference));
        Assert.False(czynsz.WithdrawalCreated);
        var insurance = after.Expenses.Payment(quarterly, "2026-09");
        Assert.Equal("Settled", insurance.Status);
        Assert.Equal((1_260m, 60m), (insurance.ActualAmountPln, insurance.Difference));
        Assert.Equal(before.Expenses.CostPln + 10m, after.Expenses.CostPln);
        Assert.Equal(before.FreeFunds.ActualPln - 3_610m, after.FreeFunds.ActualPln);
    }

    [Fact]
    public async Task SettledPayment_SurvivesDefinitionEdit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var expense = await client.CreateRecurringExpenseAsync(cancellationToken, "Czynsz", 2_400m, dayOfMonth: 5);
        await client.SettleExpensePaymentAsync(
            expense, "2026-09", account.AccountId, 2_350m, new DateOnly(2026, 9, 5), cancellationToken);

        await client.UpdateRecurringExpenseAsync(expense, "2026-09", cancellationToken, "Czynsz", 3_000m, dayOfMonth: 5);

        var september = (await client.GetPlanningMonthAsync("2026-09", cancellationToken)).Expenses;
        var settled = september.Payment(expense, "2026-09");
        Assert.Equal("Settled", settled.Status);
        Assert.Equal((2_400m, 2_350m, -50m), (settled.Amount, settled.ActualAmount, settled.Difference));
        Assert.Equal(2_950m, september.CostPln);
        var october = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses;
        Assert.Equal(3_000m, october.Payment(expense, "2026-10").Amount);
    }
}
