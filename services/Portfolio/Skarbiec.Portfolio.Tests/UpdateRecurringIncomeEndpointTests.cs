using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The update slice is called directly and asserted on the raw response where it is under test; PlanningApi helpers only arrange and read back.
public sealed class UpdateRecurringIncomeEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    private static async Task<OccurrenceView> OccurrenceAsync(HttpClient client, Guid id, string month, CancellationToken cancellationToken) =>
        Assert.Single(
            (await client.GetPlanningMonthAsync(month, cancellationToken)).Incomes.Occurrences,
            occurrence => occurrence.RecurringIncomeId == id);

    private static async Task<DefinitionView> DefinitionAsync(HttpClient client, Guid id, string month, CancellationToken cancellationToken) =>
        Assert.Single(
            (await client.GetPlanningMonthAsync(month, cancellationToken)).Incomes.Definitions,
            definition => definition.Id == id);

    private async Task<(HttpClient Client, Guid Id, Guid AccountId)> PayrollAsync(CancellationToken cancellationToken)
    {
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var id = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Wypłata Dominik", 9_200m, Start, dayKind: RecurrenceDayKind.LastBusinessDay);

        return (client, id, account.AccountId);
    }

    [Fact]
    public async Task RaiseFromMonth_KeepsEarlierMonths()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, id, accountId) = await PayrollAsync(cancellationToken);
        using var _ = client;

        var response = await client.PutAsJsonAsync(
            RecurringIncomeUri(id),
            UpdateRecurringIncomeBody(
                accountId, "2027-01", "Wypłata Dominik", 9_800m, Start, dayKind: RecurrenceDayKind.LastBusinessDay),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var december = await OccurrenceAsync(client, id, "2026-12", cancellationToken);
        Assert.Equal(new DateOnly(2026, 12, 31), december.Date);
        Assert.Equal(9_200m, december.Amount);
        var january = await OccurrenceAsync(client, id, "2027-01", cancellationToken);
        Assert.Equal(new DateOnly(2027, 1, 29), january.Date);
        Assert.Equal(9_800m, january.Amount);
        var next = (await DefinitionAsync(client, id, "2026-10", cancellationToken)).NextChange;
        Assert.NotNull(next);
        Assert.Equal("2027-01", next.EffectiveFrom);
        Assert.Equal(9_800m, next.Amount);
    }

    [Fact]
    public async Task EarlierEdit_LeavesLaterChangesUntouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, id, oldAccountId) = await PayrollAsync(cancellationToken);
        using var _ = client;
        var newAccount = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe");
        await client.UpdateRecurringIncomeAsync(
            id, oldAccountId, "2027-01", cancellationToken, "Wypłata Dominik", 9_800m, Start, dayKind: RecurrenceDayKind.LastBusinessDay);

        var response = await client.PutAsJsonAsync(
            RecurringIncomeUri(id),
            UpdateRecurringIncomeBody(
                newAccount.AccountId, "2026-11", "Wypłata Dominik", 9_200m, Start, dayKind: RecurrenceDayKind.LastBusinessDay),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(oldAccountId, (await OccurrenceAsync(client, id, "2026-10", cancellationToken)).AccountId);
        foreach (var month in new[] { "2026-11", "2026-12" })
        {
            var occurrence = await OccurrenceAsync(client, id, month, cancellationToken);
            Assert.Equal(newAccount.AccountId, occurrence.AccountId);
            Assert.Equal(9_200m, occurrence.Amount);
        }

        var january = await OccurrenceAsync(client, id, "2027-01", cancellationToken);
        Assert.Equal(oldAccountId, january.AccountId);
        Assert.Equal(9_800m, january.Amount);
    }

    [Fact]
    public async Task EditAtExistingOrBeforeStart_ReplacesVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        var id = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Pensja", 1_000m, new DateOnly(2026, 3, 1), dayOfMonth: 10);
        await client.UpdateRecurringIncomeAsync(
            id, account.AccountId, "2026-06", cancellationToken, "Pensja", 2_000m, new DateOnly(2026, 3, 1));

        var atExisting = await client.PutAsJsonAsync(
            RecurringIncomeUri(id),
            UpdateRecurringIncomeBody(account.AccountId, "2026-06", "Pensja", 3_000m, new DateOnly(2026, 3, 1)),
            cancellationToken);
        var beforeStart = await client.PutAsJsonAsync(
            RecurringIncomeUri(id),
            UpdateRecurringIncomeBody(account.AccountId, "2026-01", "Pensja", 500m, new DateOnly(2026, 3, 1)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, atExisting.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, beforeStart.StatusCode);
        Assert.Equal(500m, (await OccurrenceAsync(client, id, "2026-03", cancellationToken)).Amount);
        Assert.Equal(500m, (await OccurrenceAsync(client, id, "2026-05", cancellationToken)).Amount);
        Assert.Equal(3_000m, (await OccurrenceAsync(client, id, "2026-06", cancellationToken)).Amount);
        Assert.Equal(3_000m, (await DefinitionAsync(client, id, "2026-05", cancellationToken)).NextChange!.Amount);

        await client.DeleteRecurringIncomeChangeAsync(id, "2026-06", cancellationToken);

        Assert.Equal(500m, (await OccurrenceAsync(client, id, "2026-06", cancellationToken)).Amount);
        var again = await client.DeleteAsync(RecurringIncomeChangeUri(id, "2026-06"), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task GlobalFields_ApplyToAllMonths()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        var id = await client.CreateRecurringIncomeAsync(account.AccountId, cancellationToken, "Pensja", 1_000m, Start);
        await client.UpdateRecurringIncomeAsync(id, account.AccountId, "2026-12", cancellationToken, "Pensja", 2_000m, Start);
        var quarterly = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Premia", 700m, Start, intervalMonths: 3);

        var rename = await client.PutAsJsonAsync(
            RecurringIncomeUri(id),
            UpdateRecurringIncomeBody(account.AccountId, "2026-11", "Wypłata", 1_000m, Start),
            cancellationToken);
        var move = await client.PutAsJsonAsync(
            RecurringIncomeUri(quarterly),
            UpdateRecurringIncomeBody(account.AccountId, "2026-10", "Premia", 700m, new DateOnly(2026, 2, 1), intervalMonths: 3),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, rename.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, move.StatusCode);
        Assert.Equal("Wypłata", (await OccurrenceAsync(client, id, "2026-10", cancellationToken)).Name);
        Assert.Equal("Wypłata", (await OccurrenceAsync(client, id, "2026-12", cancellationToken)).Name);
        Assert.Equal("Wypłata", (await DefinitionAsync(client, id, "2026-10", cancellationToken)).Name);
        var noVersionAdded = await client.DeleteAsync(RecurringIncomeChangeUri(id, "2026-11"), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, noVersionAdded.StatusCode);
        Assert.Equal(2_000m, (await OccurrenceAsync(client, id, "2026-12", cancellationToken)).Amount);
        var october = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Occurrences;
        var november = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes.Occurrences;
        Assert.DoesNotContain(october, occurrence => occurrence.RecurringIncomeId == quarterly);
        Assert.Contains(november, occurrence => occurrence.RecurringIncomeId == quarterly);
    }

    [Fact]
    public async Task Validation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var current = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var other = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe");
        var otherArchived = (await client.CreateBankAccountAsync(cancellationToken, "Old account")).AccountId;
        await client.ArchiveAccountAsync(otherArchived, cancellationToken);
        var id = await client.CreateRecurringIncomeAsync(current.AccountId, cancellationToken, "Pensja", 1_000m, Start);
        await client.CreateRecurringIncomeAsync(other.AccountId, cancellationToken, "800+", 800m, Start);
        await client.ArchiveAccountAsync(current.AccountId, cancellationToken);

        var keepingArchived = await client.PutAsJsonAsync(
            RecurringIncomeUri(id),
            UpdateRecurringIncomeBody(current.AccountId, "2026-11", "Pensja", 1_200m, Start),
            cancellationToken);
        var switchedToArchived = await client.PutAsJsonAsync(
            RecurringIncomeUri(id),
            UpdateRecurringIncomeBody(otherArchived, "2026-11", "Pensja", 1_200m, Start),
            cancellationToken);
        var duplicate = await client.PutAsJsonAsync(
            RecurringIncomeUri(id),
            UpdateRecurringIncomeBody(current.AccountId, "2026-11", "800+", 1_200m, Start),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, keepingArchived.StatusCode);
        await switchedToArchived.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountArchivedErrorCode, cancellationToken);
        await duplicate.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DuplicateNameErrorCode, cancellationToken);

        var invalid = new Dictionary<string, object>
        {
            ["amount 0"] = UpdateRecurringIncomeBody(current.AccountId, "2026-11", "Pensja", 0m, Start),
            ["interval 13"] = UpdateRecurringIncomeBody(current.AccountId, "2026-11", "Pensja", 1_000m, Start, intervalMonths: 13),
            ["day 32"] = UpdateRecurringIncomeBody(current.AccountId, "2026-11", "Pensja", 1_000m, Start, dayOfMonth: 32),
            ["end before start"] = UpdateRecurringIncomeBody(
                current.AccountId, "2026-11", "Pensja", 1_000m, new DateOnly(2026, 5, 1), new DateOnly(2026, 4, 30)),
            ["effectiveFrom 2010-12"] = UpdateRecurringIncomeBody(current.AccountId, "2010-12", "Pensja", 1_000m, Start),
        };
        foreach (var (scenario, body) in invalid)
        {
            var response = await client.PutAsJsonAsync(RecurringIncomeUri(id), body, cancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{scenario} returned {(int)response.StatusCode}.");
        }
    }
}
