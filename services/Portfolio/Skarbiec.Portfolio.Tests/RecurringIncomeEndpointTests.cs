using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The create, end and delete slices are called directly and asserted on the raw response; PlanningApi helpers only arrange and read back.
public sealed class RecurringIncomeEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task InvalidRequests_Return400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        var bodies = new Dictionary<string, object>
        {
            ["amount 0"] = NewRecurringIncomeBody(account.AccountId, amount: 0m),
            ["interval 13"] = NewRecurringIncomeBody(account.AccountId, intervalMonths: 13),
            ["day 32"] = NewRecurringIncomeBody(account.AccountId, dayOfMonth: 32),
            ["last business day with a shift"] = new
            {
                name = "Wypłata",
                amount = 1_000m,
                accountId = account.AccountId,
                startDate = new DateOnly(2026, 1, 1),
                intervalMonths = 1,
                dayKind = (int)RecurrenceDayKind.LastBusinessDay,
                shift = (int)BusinessDayShift.PreviousBusinessDay
            },
            ["start before 2011"] = NewRecurringIncomeBody(account.AccountId, startDate: new DateOnly(2010, 12, 31)),
            ["end before start"] = NewRecurringIncomeBody(
                account.AccountId, startDate: new DateOnly(2026, 5, 1), endDate: new DateOnly(2026, 4, 30)),
        };

        foreach (var (scenario, body) in bodies)
        {
            var response = await client.PostAsJsonAsync(RecurringIncomesUri, body, cancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{scenario} returned {(int)response.StatusCode}.");
        }

        foreach (var month in new[] { "2026-13", "2010-12" })
        {
            var response = await client.GetAsync(PlanningMonthUri(month), cancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"month {month} returned {(int)response.StatusCode}.");
        }

        var incomes = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes;
        Assert.Empty(incomes.Definitions);
    }

    [Fact]
    public async Task DuplicateName_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        var created = await client.PostAsJsonAsync(
            RecurringIncomesUri, NewRecurringIncomeBody(account.AccountId, "800+", 800m), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var duplicate = await client.PostAsJsonAsync(
            RecurringIncomesUri, NewRecurringIncomeBody(account.AccountId, "800+", 900m), cancellationToken);

        await duplicate.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DuplicateNameErrorCode, cancellationToken);
        var definitions = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Definitions;
        Assert.Equal(800m, Assert.Single(definitions).Amount);
    }

    [Fact]
    public async Task ArchivedAccount_RejectedOnCreate_StillCountedAfterwards()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var archived = await client.CreateBankAccountAsync(cancellationToken, "Stare konto");
        await client.ArchiveAccountAsync(archived.AccountId, cancellationToken);

        var rejected = await client.PostAsJsonAsync(
            RecurringIncomesUri, NewRecurringIncomeBody(archived.AccountId), cancellationToken);

        await rejected.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountArchivedErrorCode, cancellationToken);

        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        await client.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "Wypłata", 1_000m, dayOfMonth: 10);
        await client.ArchiveAccountAsync(account.AccountId, cancellationToken);

        var october = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes;

        var occurrence = Assert.Single(october.Occurrences);
        Assert.True(occurrence.AccountArchived);
        Assert.Equal(1_000m, october.TotalPln);
        Assert.True(Assert.Single(october.Definitions).AccountArchived);
    }

    [Fact]
    public async Task End_KeepsPastPlan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        var wife = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Wypłata żona", 6_500m, new DateOnly(2026, 1, 1),
            dayOfMonth: 10, shift: BusinessDayShift.PreviousBusinessDay);

        var ended = await client.PostAsJsonAsync(
            EndRecurringIncomeUri(wife), new { endDate = new DateOnly(2026, 12, 31) }, cancellationToken);
        var beforeStart = await client.PostAsJsonAsync(
            EndRecurringIncomeUri(wife), new { endDate = new DateOnly(2025, 12, 31) }, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, beforeStart.StatusCode);
        Assert.Equal(
            new DateOnly(2026, 12, 10),
            Assert.Single((await client.GetPlanningMonthAsync("2026-12", cancellationToken)).Incomes.Occurrences).Date);
        Assert.Empty((await client.GetPlanningMonthAsync("2027-01", cancellationToken)).Incomes.Occurrences);
        Assert.Equal(
            new DateOnly(2026, 10, 9),
            Assert.Single((await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Occurrences).Date);
        var definition = Assert.Single((await client.GetPlanningMonthAsync("2027-01", cancellationToken)).Incomes.Definitions);
        Assert.Equal(new DateOnly(2026, 12, 31), definition.EndDate);
        Assert.Equal("Ended", definition.Status);
    }

    [Fact]
    public async Task Delete_RemovesIncomeAndVersions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var account = await client.CreateBankAccountAsync(cancellationToken);
        var income = await client.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "Zwrot kosztów", 300m);
        await client.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "Pensja", 500m);
        await using (var before = CreateDbContext(userId))
        {
            Assert.Equal(2, await before.Set<RecurringIncomeVersion>().CountAsync(cancellationToken));
        }

        var response = await client.DeleteAsync(RecurringIncomeUri(income), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var october = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes;
        Assert.DoesNotContain(october.Occurrences, o => o.Name == "Zwrot kosztów");
        Assert.Equal(["Pensja"], october.Definitions.Select(d => d.Name).ToArray());
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(1, await dbContext.Set<RecurringIncome>().CountAsync(cancellationToken));
        var version = await dbContext.Set<RecurringIncomeVersion>().SingleAsync(cancellationToken);
        Assert.NotEqual(income, version.RecurringIncomeId);
    }
}
