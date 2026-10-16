using System.Net;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The DELETE under test is called directly and asserted on the raw response; AccountsApi helpers only arrange.
public sealed class DeleteAccountEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Delete_AccountWithAnAsset_ReturnsAccountHasHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        await client.AddStockInAccountAsync(accountId, cancellationToken);

        var response = await client.DeleteAsync(AccountUri(accountId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountHasHistoryErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AccountUri(accountId), cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task CashHistoryBlocksDelete()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateAccountWithCashAsync(cancellationToken, balance: 100m);

        var response = await client.DeleteAsync(AccountUri(account.AccountId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountHasHistoryErrorCode, cancellationToken);
        Assert.Equal(100m, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));
    }

    [Fact]
    public async Task RemovesCashAndPublishes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateAccountWithCashAsync(cancellationToken);

        var response = await client.DeleteAsync(AccountUri(account.AccountId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync(PortfolioApi.AssetUri(account.CashAssetId), cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Delete_EmptyAccount_ReturnsNoContentAndRemovesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);

        var response = await client.DeleteAsync(AccountUri(accountId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(AccountUri(accountId), cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task OpenDepositBlocks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var mBank = await client.CreateBankAccountAsync(
            cancellationToken, "mBank", balance: 20_000m, depositedOn: DefaultTopUpDate);
        var nest = await client.CreateBankAccountAsync(cancellationToken, "Nest", SavingsVariant);
        await client.AddDepositAsync(
            cancellationToken,
            NewDepositRequest(
                startDate: new DateOnly(2026, 4, 1), termLength: 6, sourceAccountId: mBank.AccountId, returnAccountId: nest.AccountId));

        var response = await client.DeleteAsync(AccountUri(nest.AccountId), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.AccountHasOpenDepositErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AccountUri(nest.AccountId), cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Returns409_WhenRecurringIncomeTargetsAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var income = await client.CreateRecurringIncomeAsync(accountId, cancellationToken);

        var blocked = await client.DeleteAsync(AccountUri(accountId), cancellationToken);

        await blocked.AssertProblemAsync(HttpStatusCode.Conflict, PlanningApi.AccountHasRecurringIncomeErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AccountUri(accountId), cancellationToken)).StatusCode);

        await client.DeleteRecurringIncomeAsync(income, cancellationToken);
        var deleted = await client.DeleteAsync(AccountUri(accountId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Returns409_WhenIncomeOccurrenceReferencesAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningApi.PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var planned = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var settlement = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe");
        var income = await client.CreateRecurringIncomeAsync(planned.AccountId, cancellationToken);
        await client.SettleOccurrenceAsync(income, "2026-10", settlement.AccountId, 1_000m, new DateOnly(2026, 10, 10), cancellationToken);

        var blocked = await client.DeleteAsync(AccountUri(settlement.AccountId), cancellationToken);

        await blocked.AssertProblemAsync(HttpStatusCode.Conflict, PlanningApi.AccountHasIncomeOccurrencesErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AccountUri(settlement.AccountId), cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Returns409_WhenIncomeOccurrenceWasPlannedOnAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningApi.PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var planned = await client.CreateBankAccountAsync(cancellationToken, "mBank ROR");
        var current = await client.CreateBankAccountAsync(cancellationToken, "Konto firmowe");
        var income = await client.CreateRecurringIncomeAsync(planned.AccountId, cancellationToken);
        await client.SkipOccurrenceAsync(income, "2026-10", cancellationToken);
        await client.UpdateRecurringIncomeAsync(income, current.AccountId, "2026-01", cancellationToken);

        var blocked = await client.DeleteAsync(AccountUri(planned.AccountId), cancellationToken);

        await blocked.AssertProblemAsync(HttpStatusCode.Conflict, PlanningApi.AccountHasIncomeOccurrencesErrorCode, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AccountUri(planned.AccountId), cancellationToken)).StatusCode);
    }
}
