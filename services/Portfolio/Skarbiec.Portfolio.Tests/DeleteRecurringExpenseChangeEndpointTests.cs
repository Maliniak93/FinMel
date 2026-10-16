using System.Net;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The delete-change slice is called directly and asserted on the raw response; PlanningApi helpers only arrange and read back.
public sealed class DeleteRecurringExpenseChangeEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task DeletesScheduledChange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var id = await client.CreateOcToyotaAsync(cancellationToken);
        await client.UpdateRecurringExpenseAsync(
            id, "2027-01", cancellationToken, "OC Toyota", 1_320m, OcToyotaStart, intervalMonths: 12, dayOfMonth: 15);

        var firstVersion = await client.DeleteAsync(RecurringExpenseChangeUri(id, "2026-10"), cancellationToken);
        var noVersion = await client.DeleteAsync(RecurringExpenseChangeUri(id, "2026-05"), cancellationToken);
        var deleted = await client.DeleteAsync(RecurringExpenseChangeUri(id, "2027-01"), cancellationToken);

        await firstVersion.AssertProblemAsync(HttpStatusCode.Conflict, FirstVersionErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, noVersion.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        foreach (var month in new[] { "2027-01", "2027-02", "2027-03" })
        {
            Assert.Equal(200m, (await client.ExpenseDefinitionAsync(id, month, cancellationToken)).ReservePln);
        }

        Assert.Null((await client.ExpenseDefinitionAsync(id, "2026-10", cancellationToken)).NextChange);
    }
}
