using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class PlanningMonthReserveTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static async Task AssertReserveAsync(
        HttpClient client, string month, decimal monthPln, decimal balancePln, CancellationToken cancellationToken)
    {
        var reserve = (await client.GetAllocationAsync(month, cancellationToken)).Reserve;

        Assert.NotNull(reserve);
        Assert.Equal(monthPln, reserve.MonthPln);
        Assert.Equal(balancePln, reserve.BalancePln);
    }

    [Fact]
    public async Task IrregularExpense_ReserveAndBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateRecurringExpenseAsync(cancellationToken, "Życie", 3_000m, dayOfMonth: 1);
        await client.CreateOcToyotaAsync(cancellationToken);

        Assert.Null((await client.GetAllocationAsync("2026-09", cancellationToken)).Reserve);
        await AssertReserveAsync(client, "2026-10", 200m, 200m, cancellationToken);
        await AssertReserveAsync(client, "2026-12", 200m, 600m, cancellationToken);
        await AssertReserveAsync(client, "2027-03", 200m, 0m, cancellationToken);
        await AssertReserveAsync(client, "2027-04", 100m, 100m, cancellationToken);
    }

    [Fact]
    public async Task ReserveNotSplitAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAllocationSetupAsync(cancellationToken);
        var savings = await client.CreateBankAccountAsync(cancellationToken, "Konto oszczędnościowe", SavingsVariant);
        await client.CreateRecurringExpenseAsync(cancellationToken, "Życie", 3_000m, dayOfMonth: 1);
        await client.CreateOcToyotaAsync(cancellationToken);
        await client.SaveAllocationPlanAsync("2026-10", cancellationToken, FirstAllocationPlan());
        var without = await client.GetAllocationAsync("2026-10", cancellationToken);

        await client.SaveAllocationPlanWithReserveAsync("2026-10", savings.AccountId, cancellationToken, FirstAllocationPlan());
        var with = await client.GetAllocationAsync("2026-10", cancellationToken);

        Assert.Equal(9_043.27m, with.ForecastPln);
        Assert.Equal(without.ForecastPln, with.ForecastPln);
        Assert.Equal(
            without.Buckets.Select(b => (b.Name, b.AllocatedPln, b.Underfunded)).ToList(),
            with.Buckets.Select(b => (b.Name, b.AllocatedPln, b.Underfunded)).ToList());
        Assert.Equal(200m, with.Reserve?.MonthPln);
    }

    [Fact]
    public async Task DeletedExpense_LeavesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ocToyota = await client.CreateOcToyotaAsync(cancellationToken);
        await AssertReserveAsync(client, "2026-12", 200m, 600m, cancellationToken);

        await client.DeleteRecurringExpenseAsync(ocToyota, cancellationToken);

        Assert.Null((await client.GetAllocationAsync("2026-12", cancellationToken)).Reserve);
    }

    [Fact]
    public async Task ReserveAccount_FromPlanVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAllocationSetupAsync(cancellationToken);
        await client.CreateOcToyotaAsync(cancellationToken);
        var savings = await client.CreateBankAccountAsync(cancellationToken, "Konto oszczędnościowe", SavingsVariant);
        await client.SaveAllocationPlanWithReserveAsync("2026-10", savings.AccountId, cancellationToken);

        var october = (await client.GetAllocationAsync("2026-10", cancellationToken)).Reserve;
        Assert.NotNull(october);
        Assert.Equal(savings.AccountId, october.AccountId);
        Assert.Equal("Konto oszczędnościowe", october.AccountName);
        Assert.Null((await client.GetAllocationAsync("2026-09", cancellationToken)).Reserve);

        var unknown = await client.PutAsJsonAsync(
            AllocationPlanVersionUri("2026-10"),
            new { buckets = Array.Empty<object>(), reserveAccountId = Guid.NewGuid() },
            cancellationToken);
        await unknown.AssertProblemAsync(HttpStatusCode.NotFound, "NotFound.Account", cancellationToken);

        var archived = await client.CreateBankAccountAsync(cancellationToken, "Stare konto", SavingsVariant);
        await client.ArchiveAccountAsync(archived.AccountId, cancellationToken);
        var changed = await client.PutAsJsonAsync(
            AllocationPlanVersionUri("2026-11"),
            new { buckets = Array.Empty<object>(), reserveAccountId = archived.AccountId },
            cancellationToken);
        await changed.AssertProblemAsync(HttpStatusCode.Conflict, "Conflict.AccountArchived", cancellationToken);
    }
}
