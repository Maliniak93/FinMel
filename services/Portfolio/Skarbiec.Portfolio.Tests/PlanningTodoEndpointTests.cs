using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.TransferPlan;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningTodoEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static readonly DateTimeOffset Mid2027MarchTen = new(2027, 3, 10, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OverdueIncomes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var second = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Druga", 800m, new DateOnly(2026, 1, 1), dayOfMonth: 2);
        var tenth = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Dziesiąta", 1_500m, new DateOnly(2026, 1, 1), dayOfMonth: 10);
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero));

        var todo = await client.GetTodoAsync(cancellationToken);

        Assert.Equal(
            [
                (OverdueIncomeKind, "Druga", new DateOnly(2026, 11, 2), 800m, "PLN", 13),
                (OverdueIncomeKind, "Dziesiąta", new DateOnly(2026, 11, 10), 1_500m, "PLN", 5)
            ],
            todo.Items.Select(i => (i.Kind, i.Name, i.Date, i.Amount, i.Currency, i.DaysOverdue)));

        await client.SettleOccurrenceAsync(second, "2026-11", account.AccountId, 800m, new DateOnly(2026, 11, 2), cancellationToken);
        await client.SkipOccurrenceAsync(tenth, "2026-11", cancellationToken);

        Assert.DoesNotContain((await client.GetTodoAsync(cancellationToken)).Items, i => i.Kind == OverdueIncomeKind);
    }

    [Fact]
    public async Task OneOffIncome_Overdue_IsListed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var overdue = await client.CreateOneOffIncomeAsync(account.AccountId, cancellationToken, "Premia", 500m, new DateOnly(2026, 9, 10));
        var settled = await client.CreateOneOffIncomeAsync(account.AccountId, cancellationToken, "Zwrot", 300m, new DateOnly(2026, 9, 12));
        await client.SettleOneOffIncomeAsync(settled, account.AccountId, 300m, new DateOnly(2026, 9, 12), cancellationToken);

        var todo = await client.GetTodoAsync(cancellationToken);

        var item = Assert.Single(todo.Items, i => i.Name == "Premia");
        Assert.Equal((new DateOnly(2026, 9, 10), 500m, "PLN"), (item.Date, item.Amount, item.Currency));
        Assert.DoesNotContain(todo.Items, i => i.Name == "Zwrot");

        await client.SettleOneOffIncomeAsync(overdue, account.AccountId, 500m, new DateOnly(2026, 9, 10), cancellationToken);

        Assert.DoesNotContain((await client.GetTodoAsync(cancellationToken)).Items, i => i.Name == "Premia");
    }

    [Fact]
    public async Task TransferPlans()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 8, 15, 10, 0, 0, TimeSpan.Zero));
        var ike = await client.CreateAccountAsync(cancellationToken, "IKE mBank", AccountType.Brokerage, TaxWrapper.Ike);
        var ikze = await client.CreateAccountAsync(cancellationToken, "IKZE Obligacje", AccountType.Bonds, TaxWrapper.Ikze);
        await client.SaveAllocationPlanAsync(
            "2026-08",
            cancellationToken,
            AllocationRow("IKE", IkeKind, FixedShare, 2_260m, accountId: ike),
            AllocationRow("IKZE", IkzeKind, FixedShare, 2_304m, accountId: ikze),
            AllocationRow("Inwestycje", InvestmentsKind, PercentShare, 100m, isRemainder: true));
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero));
        await client.CreateAllocationSetupAsync(cancellationToken, 6_543.27m);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        var august = await client.GetTransferPlanAsync("2026-08", cancellationToken);
        Assert.True(august.Status.Is(TransferPlanStatus.Preview));
        Assert.True(august.ToInvestPln > 0m);
        Factory.Clock.SetUtcNow(CreationDayUtc);
        await client.AcceptTransferPlanAsync("2026-09", cancellationToken);
        var september = await client.GetTransferPlanAsync("2026-09", cancellationToken);
        Assert.Equal(4, september.Rows.Count);
        foreach (var row in september.Rows.Take(2))
        {
            await client.CheckTransferPlanRowAsync(row.Id!.Value, true, cancellationToken);
        }

        var todo = await client.GetTodoAsync(cancellationToken);

        Assert.Equal(
            [
                (TransferPlanUncheckedKind, "2026-09", (decimal?)null, 2, 4),
                (TransferPlanToAcceptKind, "2026-10", 6_543.27m, null, null)
            ],
            todo.Items
                .Where(i => i.Kind != OverdueIncomeKind)
                .Select(i => (i.Kind, i.Month, i.Amount, i.UncheckedRows, i.TotalRows)));
    }

    [Fact]
    public async Task TransferPlans_CompletedSkippedAndNone_AddNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero));
        await client.CreateTransferPlanSetupAsync(cancellationToken, "2026-08");
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        Factory.Clock.SetUtcNow(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero));
        await client.AcceptTransferPlanAsync("2026-08", cancellationToken);
        foreach (var row in (await client.GetTransferPlanAsync("2026-08", cancellationToken)).Rows)
        {
            await client.CheckTransferPlanRowAsync(row.Id!.Value, true, cancellationToken);
        }

        await client.SkipTransferPlanAsync("2026-09", cancellationToken);

        var todo = await client.GetTodoAsync(cancellationToken);

        Assert.DoesNotContain(todo.Items, i => i.Kind is TransferPlanToAcceptKind or TransferPlanUncheckedKind);
    }

    [Fact]
    public async Task TopUp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var setup = await client.CreateTransferPlanBenefitSetupAsync(cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        await client.AcceptTransferPlanAsync("2026-10", cancellationToken);
        var rows = (await client.GetTransferPlanAsync("2026-10", cancellationToken)).Rows;
        foreach (var row in rows.Take(rows.Count - 1))
        {
            await client.CheckTransferPlanRowAsync(row.Id!.Value, true, cancellationToken);
        }

        await client.SettleBenefitAsync(setup, 1_000m, cancellationToken);

        var todo = await client.GetTodoAsync(cancellationToken);

        Assert.Equal(
            [
                (TransferPlanToAcceptKind, "2026-10", (decimal?)200m, (bool?)true, (int?)null, (int?)null),
                (TransferPlanUncheckedKind, "2026-10", null, null, 1, 4)
            ],
            todo.Items.Select(i => (i.Kind, i.Month, i.Amount, i.IsTopUp, i.UncheckedRows, i.TotalRows)));

        await client.AcceptTransferPlanTopUpAsync("2026-10", cancellationToken);
        foreach (var row in (await client.GetTransferPlanAsync("2026-10", cancellationToken)).Rows.Where(r => r.Checked == false))
        {
            await client.CheckTransferPlanRowAsync(row.Id!.Value, true, cancellationToken);
        }

        Assert.Empty((await client.GetTodoAsync(cancellationToken)).Items);
    }

    [Fact]
    public async Task UpcomingPayments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(Mid2027MarchTen);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateOcToyotaAsync(cancellationToken);
        await client.CreateRecurringExpenseAsync(
            cancellationToken, "Przegląd", 400m, new DateOnly(2027, 3, 12), intervalMonths: 6, dayOfMonth: 12);
        await client.CreateRecurringExpenseAsync(cancellationToken, "Rachunki", 1_000m, dayOfMonth: 12);

        var onTenth = await client.GetTodoAsync(cancellationToken);
        Factory.Clock.SetUtcNow(new DateTimeOffset(2027, 3, 7, 10, 0, 0, TimeSpan.Zero));
        var onSeventh = await client.GetTodoAsync(cancellationToken);
        Factory.Clock.SetUtcNow(new DateTimeOffset(2027, 3, 15, 10, 0, 0, TimeSpan.Zero));
        var onFifteenth = await client.GetTodoAsync(cancellationToken);
        Factory.Clock.SetUtcNow(new DateTimeOffset(2027, 3, 16, 10, 0, 0, TimeSpan.Zero));
        var onSixteenth = await client.GetTodoAsync(cancellationToken);
        await client.SavePlanningSettingsAsync(cancellationToken, paymentLeadDays: 0);
        Factory.Clock.SetUtcNow(Mid2027MarchTen);
        var withoutLead = await client.GetTodoAsync(cancellationToken);

        Assert.Equal(
            [
                (UpcomingPaymentKind, "Przegląd", new DateOnly(2027, 3, 12), 400m, 2),
                (UpcomingPaymentKind, "OC Toyota", new DateOnly(2027, 3, 15), 1_200m, 5)
            ],
            onTenth.Items.Select(i => (i.Kind, i.Name, i.Date, i.Amount, i.DaysUntil)));
        Assert.Equal([("Przegląd", 5)], onSeventh.Items.Select(i => (i.Name, i.DaysUntil)));
        Assert.Equal([("OC Toyota", 0)], onFifteenth.Items.Select(i => (i.Name, i.DaysUntil)));
        Assert.Empty(onSixteenth.Items);
        Assert.Empty(withoutLead.Items);
    }

    [Fact]
    public async Task Order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        await client.CreateTransferPlanSetupAsync(cancellationToken);
        await SeedReplicasAsync(userId, cancellationToken, TransferTargets, TransferValues, TransferValuationDate);
        var premiaAccount = await client.CreateBankAccountAsync(cancellationToken, "Konto premii");
        await client.CreateRecurringIncomeAsync(premiaAccount.AccountId, cancellationToken, "Premia", 500m, dayOfMonth: 18);
        await client.CreateRecurringExpenseAsync(
            cancellationToken, "Przegląd", 400m, new DateOnly(2026, 10, 25), intervalMonths: 6, dayOfMonth: 25);
        Factory.Clock.SetUtcNow(PlanningTodayUtc);

        var todo = await client.GetTodoAsync(cancellationToken);

        Assert.Equal(
            [OverdueIncomeKind, TransferPlanToAcceptKind, UpcomingPaymentKind],
            todo.Items.Select(i => i.Kind));
    }
}
