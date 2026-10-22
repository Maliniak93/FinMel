using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningTodoTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task TodoAndSettings_AsUserB_SeeOnlyOwn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        using var clientA = Factory.CreateAuthenticatedClient(userA);
        using var clientB = Factory.CreateAuthenticatedClient(userB);
        var accountA = await clientA.CreateBankAccountAsync(cancellationToken, "Konto premii");
        await clientA.CreateRecurringIncomeAsync(accountA.AccountId, cancellationToken, "Premia A", 500m, dayOfMonth: 18);
        await clientA.CreateRecurringExpenseAsync(
            cancellationToken, "Przegląd A", 400m, new DateOnly(2026, 10, 25), intervalMonths: 6, dayOfMonth: 25);
        await clientA.SavePlanningSettingsAsync(cancellationToken, paymentLeadDays: 14);
        var accountB = await clientB.CreateBankAccountAsync(cancellationToken, "Konto B");
        await clientB.CreateRecurringIncomeAsync(accountB.AccountId, cancellationToken, "Premia B", 700m, dayOfMonth: 19);
        await clientB.CreateRecurringExpenseAsync(
            cancellationToken, "Przegląd B", 300m, new DateOnly(2026, 10, 28), intervalMonths: 6, dayOfMonth: 28);
        await clientB.SavePlanningSettingsAsync(cancellationToken, paymentLeadDays: 10);
        Factory.Clock.SetUtcNow(PlanningTodayUtc);

        var todoA = await clientA.GetTodoAsync(cancellationToken);
        var todoB = await clientB.GetTodoAsync(cancellationToken);

        Assert.Equal(
            [(OverdueIncomeKind, "Premia A"), (UpcomingPaymentKind, "Przegląd A")],
            todoA.Items.Select(i => (i.Kind, i.Name)));
        Assert.Equal(
            [(OverdueIncomeKind, "Premia B"), (UpcomingPaymentKind, "Przegląd B")],
            todoB.Items.Select(i => (i.Kind, i.Name)));
        Assert.Equal(14, todoA.PaymentLeadDays);
        Assert.Equal(10, todoB.PaymentLeadDays);
        Assert.Equal(new PlanningSettingsView(14), await clientA.GetPlanningSettingsAsync(cancellationToken));
        Assert.Equal(new PlanningSettingsView(10), await clientB.GetPlanningSettingsAsync(cancellationToken));
    }
}
