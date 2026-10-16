using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;

namespace Skarbiec.Portfolio.Tests;

// Incomes are arranged through PlanningApi; the overdue list is read raw in the PLN-only fact and through its helper elsewhere.
public sealed class PlanningOverdueEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task ListsPendingSinceCreation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var benefit = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "800+", 800m, new DateOnly(2024, 6, 10), dayOfMonth: 10);
        var wife = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Wypłata żona", 6_500m, new DateOnly(2026, 1, 1),
            dayOfMonth: 10, shift: BusinessDayShift.PreviousBusinessDay);
        Factory.Clock.SetUtcNow(AfterDueDayUtc);

        var overdue = await client.GetOverdueOccurrencesAsync(cancellationToken);

        Assert.Equal(
            [("800+", new DateOnly(2026, 11, 10), "2026-11"), ("Wypłata żona", new DateOnly(2026, 11, 10), "2026-11")],
            overdue.Items.Select(i => (i.Name, i.Date, i.NominalMonth)).ToArray());
        var first = overdue.Items[0];
        Assert.Equal((benefit, account.AccountId, "mBank ROR", false), (first.RecurringIncomeId, first.AccountId, first.AccountName, first.AccountArchived));
        Assert.Equal((800m, "PLN"), (first.Amount, first.Currency));
        Assert.Equal(wife, overdue.Items[1].RecurringIncomeId);

        await client.SettleOccurrenceAsync(benefit, "2026-11", account.AccountId, 800m, new DateOnly(2026, 11, 10), cancellationToken);
        await client.SkipOccurrenceAsync(wife, "2026-11", cancellationToken);

        Assert.Empty((await client.GetOverdueOccurrencesAsync(cancellationToken)).Items);
    }

    [Fact]
    public async Task CreatedOn_NeverMoves()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var id = await client.CreateRecurringIncomeAsync(
            account.AccountId, cancellationToken, "Pensja", 1_000m, new DateOnly(2026, 6, 10), dayOfMonth: 10);
        await client.UpdateRecurringIncomeAsync(
            id, account.AccountId, "2026-01", cancellationToken, "Pensja", 1_000m, new DateOnly(2026, 1, 1));
        Factory.Clock.SetUtcNow(AfterDueDayUtc);

        var overdue = await client.GetOverdueOccurrencesAsync(cancellationToken);

        var item = Assert.Single(overdue.Items);
        Assert.Equal(new DateOnly(2026, 11, 10), item.Date);
    }

    [Fact]
    public async Task ForeignIncome_DoesNotCallFx()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(CreationDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var eur = await client.CreateBankAccountAsync(cancellationToken, "Revolut EUR", currency: "EUR");
        await client.CreateRecurringIncomeAsync(eur.AccountId, cancellationToken, "Najem EUR", 1_000m, dayOfMonth: 10);
        Factory.Clock.SetUtcNow(AfterDueDayUtc);

        var response = await client.GetAsync(OverdueUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Factory.InstrumentQuoteLookupClient.RateCalls);
        Assert.Single((await response.Content.ReadFromJsonAsync<OverdueView>(cancellationToken))!.Items);
    }
}
