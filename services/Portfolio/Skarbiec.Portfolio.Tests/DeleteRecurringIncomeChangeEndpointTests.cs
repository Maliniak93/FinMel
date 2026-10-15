using System.Net;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The delete-change slice is called directly and asserted on the raw response; PlanningApi helpers only arrange and read back.
public sealed class DeleteRecurringIncomeChangeEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task DeletesScheduledChange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var start = new DateOnly(2026, 1, 1);
        var id = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Wypłata Dominik", 9_200m, start, dayKind: RecurrenceDayKind.LastBusinessDay);
        await client.UpdateRecurringIncomeAsync(
            id, account.AccountId, "2027-01", cancellationToken, "Wypłata Dominik", 9_800m, start, dayKind: RecurrenceDayKind.LastBusinessDay);

        var firstVersion = await client.DeleteAsync(RecurringIncomeChangeUri(id, "2026-01"), cancellationToken);
        var noVersion = await client.DeleteAsync(RecurringIncomeChangeUri(id, "2026-05"), cancellationToken);
        var deleted = await client.DeleteAsync(RecurringIncomeChangeUri(id, "2027-01"), cancellationToken);

        await firstVersion.AssertProblemAsync(HttpStatusCode.Conflict, FirstVersionErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, noVersion.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var january = Assert.Single((await client.GetPlanningMonthAsync("2027-01", cancellationToken)).Incomes.Occurrences);
        Assert.Equal(9_200m, january.Amount);
        var definition = Assert.Single((await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.Definitions);
        Assert.Null(definition.NextChange);
    }
}
