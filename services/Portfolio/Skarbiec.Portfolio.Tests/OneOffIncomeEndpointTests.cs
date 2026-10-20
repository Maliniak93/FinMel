using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// The create, update and delete slices are called directly and asserted on the raw response; PlanningApi helpers only arrange and read back.
public sealed class OneOffIncomeEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Create_AppearsInItsMonthOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");

        var response = await client.PostAsJsonAsync(
            OneOffIncomesUri, NewOneOffIncomeBody(account.AccountId, "Premia", 8_000m, new DateOnly(2026, 12, 15)), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var december = (await client.GetPlanningMonthAsync("2026-12", cancellationToken)).Incomes;
        var row = Assert.Single(december.OneOffs!);
        Assert.Equal(("Premia", 8_000m, new DateOnly(2026, 12, 15), account.AccountId), (row.Name, row.Amount, row.Date, row.AccountId));
        Assert.Equal("mBank ROR", row.AccountName);
        Assert.Equal("PLN", row.Currency);
        Assert.True(row.InMonth);
        foreach (var month in new[] { "2026-10", "2026-11", "2027-01" })
        {
            var other = Assert.Single((await client.GetPlanningMonthAsync(month, cancellationToken)).Incomes.OneOffs!);
            Assert.Equal(row.Id, other.Id);
            Assert.False(other.InMonth, month);
        }
    }

    [Fact]
    public async Task Create_AllowsDuplicateNames()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);

        var first = await client.PostAsJsonAsync(OneOffIncomesUri, NewOneOffIncomeBody(account.AccountId, "Premia"), cancellationToken);
        var second = await client.PostAsJsonAsync(OneOffIncomesUri, NewOneOffIncomeBody(account.AccountId, "Premia"), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(2, (await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes.OneOffs!.Count);
    }

    [Fact]
    public async Task Create_InvalidRequests()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        var bodies = new Dictionary<string, object>
        {
            ["empty name"] = NewOneOffIncomeBody(account.AccountId, name: ""),
            ["201-character name"] = NewOneOffIncomeBody(account.AccountId, name: new string('a', 201)),
            ["amount 0"] = NewOneOffIncomeBody(account.AccountId, amount: 0m),
            ["negative amount"] = NewOneOffIncomeBody(account.AccountId, amount: -1m),
            ["date before 2011"] = NewOneOffIncomeBody(account.AccountId, date: new DateOnly(2010, 12, 31)),
            ["date after 2099"] = NewOneOffIncomeBody(account.AccountId, date: new DateOnly(2100, 1, 1)),
            ["no account"] = new { name = "Premia", amount = 1_000m, date = new DateOnly(2026, 11, 15) },
        };

        foreach (var (scenario, body) in bodies)
        {
            var response = await client.PostAsJsonAsync(OneOffIncomesUri, body, cancellationToken);

            Assert.True(
                response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
                $"{scenario} returned {(int)response.StatusCode}.");
        }

        var unknown = await client.PostAsJsonAsync(OneOffIncomesUri, NewOneOffIncomeBody(Guid.NewGuid()), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var archived = await client.CreateBankAccountAsync(cancellationToken, "Stare konto");
        await client.ArchiveAccountAsync(archived.AccountId, cancellationToken);
        var onArchived = await client.PostAsJsonAsync(OneOffIncomesUri, NewOneOffIncomeBody(archived.AccountId), cancellationToken);
        Assert.True(
            onArchived.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Conflict,
            $"archived account returned {(int)onArchived.StatusCode}.");

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var foreign = await stranger.PostAsJsonAsync(OneOffIncomesUri, NewOneOffIncomeBody(account.AccountId), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        Assert.Empty((await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes.OneOffs!);
        Assert.Empty((await stranger.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes.OneOffs!);
    }

    [Fact]
    public async Task Update_ChangesAllFields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var first = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var second = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe");
        var id = await client.CreateOneOffIncomeAsync(first.AccountId, cancellationToken, "Premia", 1_000m, new DateOnly(2026, 11, 15));

        var response = await client.PutAsJsonAsync(
            OneOffIncomeUri(id),
            NewOneOffIncomeBody(second.AccountId, "Premia roczna", 2_500m, new DateOnly(2027, 1, 20)),
            cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"update returned {(int)response.StatusCode}.");
        var january = Assert.Single((await client.GetPlanningMonthAsync("2027-01", cancellationToken)).Incomes.OneOffs!);
        Assert.Equal(
            (id, "Premia roczna", 2_500m, new DateOnly(2027, 1, 20), second.AccountId, true),
            (january.Id, january.Name, january.Amount, january.Date, january.AccountId, january.InMonth));
        Assert.Equal("Konto firmowe", january.AccountName);
    }

    [Fact]
    public async Task Delete_RemovesIncome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        var id = await client.CreateOneOffIncomeAsync(account.AccountId, cancellationToken);
        var kept = await client.CreateOneOffIncomeAsync(account.AccountId, cancellationToken, "Zwrot");

        var response = await client.DeleteAsync(OneOffIncomeUri(id), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var remaining = Assert.Single((await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes.OneOffs!);
        Assert.Equal(kept, remaining.Id);
    }

    [Fact]
    public async Task Update_WhenSettled_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var id = await client.CreateOneOffIncomeAsync(account.AccountId, cancellationToken, "Premia", 1_000m, new DateOnly(2026, 10, 10));
        await client.SettleOneOffIncomeAsync(id, account.AccountId, 1_000m, new DateOnly(2026, 10, 10), cancellationToken);

        var response = await client.PutAsJsonAsync(
            OneOffIncomeUri(id), NewOneOffIncomeBody(account.AccountId, "Zmieniona", 2_000m, new DateOnly(2026, 10, 11)), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, OneOffIncomeSettledErrorCode, cancellationToken);
        var row = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.OneOff(id);
        Assert.Equal(("Premia", 1_000m), (row.Name, row.Amount));
    }

    [Fact]
    public async Task Delete_WhenSettled_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var id = await client.CreateOneOffIncomeAsync(account.AccountId, cancellationToken, "Premia", 1_000m, new DateOnly(2026, 10, 10));
        await client.SettleOneOffIncomeAsync(id, account.AccountId, 1_000m, new DateOnly(2026, 10, 10), cancellationToken);

        var response = await client.DeleteAsync(OneOffIncomeUri(id), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, OneOffIncomeSettledErrorCode, cancellationToken);
        Assert.Equal("Settled", (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.OneOff(id).Status);
    }

    [Fact]
    public async Task EditAfterUndo_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var id = await client.CreateOneOffIncomeAsync(account.AccountId, cancellationToken, "Premia", 1_000m, new DateOnly(2026, 10, 10));
        await client.SettleOneOffIncomeAsync(id, account.AccountId, 1_000m, new DateOnly(2026, 10, 10), cancellationToken);
        await client.UndoOneOffIncomeSettlementAsync(id, cancellationToken);

        var update = await client.PutAsJsonAsync(
            OneOffIncomeUri(id), NewOneOffIncomeBody(account.AccountId, "Zmieniona", 2_000m, new DateOnly(2026, 10, 11)), cancellationToken);

        Assert.True(update.IsSuccessStatusCode, $"update returned {(int)update.StatusCode}.");
        Assert.Equal("Zmieniona", (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Incomes.OneOff(id).Name);
        var delete = await client.DeleteAsync(OneOffIncomeUri(id), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task UnknownId_Returns404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken);
        var existing = await client.CreateOneOffIncomeAsync(account.AccountId, cancellationToken);
        var unknown = Guid.NewGuid();

        var update = await client.PutAsJsonAsync(OneOffIncomeUri(unknown), NewOneOffIncomeBody(account.AccountId), cancellationToken);
        var delete = await client.DeleteAsync(OneOffIncomeUri(unknown), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(existing, Assert.Single((await client.GetPlanningMonthAsync("2026-11", cancellationToken)).Incomes.OneOffs!).Id);
    }
}
