using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The create, update and delete slices are called directly and asserted on the raw response; PlanningApi helpers only arrange and read back.
public sealed class OneOffExpenseEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Create_AppearsInItsMonthOnly_AllowsPastDateAndDuplicateNames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var past = await client.PostAsJsonAsync(
            OneOffExpensesUri, NewOneOffExpenseBody("Remont", 700m, new DateOnly(2026, 3, 5)), cancellationToken);
        var first = await client.PostAsJsonAsync(
            OneOffExpensesUri, NewOneOffExpenseBody("Wakacje", 1_200m, new DateOnly(2026, 11, 10)), cancellationToken);
        var duplicate = await client.PostAsJsonAsync(
            OneOffExpensesUri, NewOneOffExpenseBody("Wakacje", 300m, new DateOnly(2026, 11, 12)), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, past.StatusCode);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        var november = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Expenses.OneOffs!;
        Assert.Equal(3, november.Count);
        Assert.Equal(["Wakacje", "Wakacje"], november.Where(o => o.InMonth).Select(o => o.Name).ToArray());
        var march = Assert.Single(november, o => o.Name == "Remont");
        Assert.False(march.InMonth);
        Assert.Equal((700m, new DateOnly(2026, 3, 5)), (march.Amount, march.Date));
    }

    [Fact]
    public async Task Create_WithAccount_ReturnsAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var mbank = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var company = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe");

        var created = await client.PostAsJsonAsync(
            OneOffExpensesUri, NewOneOffExpenseBody("Wakacje", 300m, new DateOnly(2026, 11, 10), mbank.AccountId), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("id").GetGuid();
        var row = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Expenses.OneOff(id);
        Assert.Equal((mbank.AccountId, "mBank ROR", "Pending"), (row.AccountId, row.AccountName, row.Status));

        var updated = await client.PutAsJsonAsync(
            OneOffExpenseUri(id), NewOneOffExpenseBody("Wakacje", 300m, new DateOnly(2026, 11, 10), company.AccountId), cancellationToken);

        Assert.True(updated.IsSuccessStatusCode, $"update returned {(int)updated.StatusCode}.");
        var moved = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Expenses.OneOff(id);
        Assert.Equal((company.AccountId, "Konto firmowe"), (moved.AccountId, moved.AccountName));
    }

    [Fact]
    public async Task Create_ArchivedAccount_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var archived = await client.CreateBankAccountAsync(cancellationToken, "Stare konto");
        await client.ArchiveAccountAsync(archived.AccountId, cancellationToken);
        var id = await client.CreateOneOffExpenseAsync(cancellationToken);

        var create = await client.PostAsJsonAsync(
            OneOffExpensesUri, NewOneOffExpenseBody(accountId: archived.AccountId), cancellationToken);
        var update = await client.PutAsJsonAsync(
            OneOffExpenseUri(id), NewOneOffExpenseBody(accountId: archived.AccountId), cancellationToken);

        await create.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountArchivedErrorCode, cancellationToken);
        await update.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountArchivedErrorCode, cancellationToken);
        Assert.Null((await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Expenses.OneOff(id).AccountId);
    }

    [Fact]
    public async Task Update_Settled_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settled = await client.CreateOneOffExpenseWithdrawalAsync(cancellationToken);

        var response = await client.PutAsJsonAsync(
            OneOffExpenseUri(settled.ExpenseId), NewOneOffExpenseBody("Inne", 500m, new DateOnly(2026, 10, 12)), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, OneOffExpenseSettledErrorCode, cancellationToken);
        var row = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(settled.ExpenseId);
        Assert.Equal(("Wakacje", 100m), (row.Name, row.Amount));
    }

    [Fact]
    public async Task Delete_Settled_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settled = await client.CreateOneOffExpenseWithdrawalAsync(cancellationToken);

        var response = await client.DeleteAsync(OneOffExpenseUri(settled.ExpenseId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, OneOffExpenseSettledErrorCode, cancellationToken);
        Assert.Equal("Settled", (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(settled.ExpenseId).Status);
    }

    [Fact]
    public async Task DeleteAccount_ClearsDefaultAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var id = await client.CreateOneOffExpenseAsync(cancellationToken, "Wakacje", 300m, new DateOnly(2026, 11, 10), account.AccountId);

        var response = await client.DeleteAsync(AccountUri(account.AccountId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Expenses.OneOff(id);
        Assert.Null(row.AccountId);
        Assert.Null(row.AccountName);
    }

    [Fact]
    public async Task DeleteAccount_WithSettlement_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var id = await client.CreateOneOffExpenseAsync(cancellationToken, "Wakacje", 100m, new DateOnly(2026, 10, 10));
        await client.SettleOneOffExpenseAsync(id, account.AccountId, 90m, ExpenseSettlementDay, cancellationToken);

        var response = await client.DeleteAsync(AccountUri(account.AccountId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, AccountHasExpenseSettlementsErrorCode, cancellationToken);
    }

    [Fact]
    public async Task Create_InvalidRequests_Return400AndStoreNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var bodies = new Dictionary<string, object>
        {
            ["empty name"] = NewOneOffExpenseBody(name: ""),
            ["201-character name"] = NewOneOffExpenseBody(name: new string('a', 201)),
            ["amount 0"] = NewOneOffExpenseBody(amount: 0m),
            ["negative amount"] = NewOneOffExpenseBody(amount: -5m),
            ["date before 2011"] = NewOneOffExpenseBody(date: new DateOnly(2010, 12, 31)),
            ["date after 2099"] = NewOneOffExpenseBody(date: new DateOnly(2100, 1, 1)),
        };

        foreach (var (scenario, body) in bodies)
        {
            var response = await client.PostAsJsonAsync(OneOffExpensesUri, body, cancellationToken);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{scenario} returned {(int)response.StatusCode}.");
        }

        Assert.Empty((await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Expenses.OneOffs!);
    }

    [Fact]
    public async Task Update_ChangesFields_AndValidates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var id = await client.CreateOneOffExpenseAsync(cancellationToken, "Wakacje", 1_000m, new DateOnly(2026, 11, 15));

        var invalid = await client.PutAsJsonAsync(OneOffExpenseUri(id), NewOneOffExpenseBody(amount: 0m), cancellationToken);
        var response = await client.PutAsJsonAsync(
            OneOffExpenseUri(id), NewOneOffExpenseBody("Remont", 2_500m, new DateOnly(2027, 1, 20)), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.True(response.IsSuccessStatusCode, $"update returned {(int)response.StatusCode}.");
        var january = Assert.Single((await client.GetPlanningMonthAsync("2027-01", cancellationToken)).Expenses.OneOffs!);
        Assert.Equal(
            (id, "Remont", 2_500m, new DateOnly(2027, 1, 20), true),
            (january.Id, january.Name, january.Amount, january.Date, january.InMonth));
    }

    [Fact]
    public async Task Delete_RemovesExpense_UnknownId404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var id = await client.CreateOneOffExpenseAsync(cancellationToken);

        var deleted = await client.DeleteAsync(OneOffExpenseUri(id), cancellationToken);
        var unknownDelete = await client.DeleteAsync(OneOffExpenseUri(Guid.NewGuid()), cancellationToken);
        var unknownUpdate = await client.PutAsJsonAsync(OneOffExpenseUri(Guid.NewGuid()), NewOneOffExpenseBody(), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownUpdate.StatusCode);
        Assert.Empty((await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Expenses.OneOffs!);
    }
}
