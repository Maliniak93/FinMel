using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
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
}
