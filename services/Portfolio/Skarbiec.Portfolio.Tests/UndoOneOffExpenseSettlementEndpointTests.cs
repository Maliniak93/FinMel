using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PlanningApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The undo slice is called directly and asserted on the raw response; PlanningApi helpers only arrange and read back.
public sealed class UndoOneOffExpenseSettlementEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Undo_RemovesWithdrawal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settled = await client.CreateOneOffExpenseWithdrawalAsync(cancellationToken);
        Assert.Equal(910m, await client.GetCashBalanceAsync(settled.Account.AccountId, cancellationToken));

        var response = await client.DeleteAsync(OneOffExpenseSettlementUri(settled.ExpenseId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(settled.ExpenseId);
        Assert.Equal("Pending", row.Status);
        Assert.Null(row.ActualAmount);
        Assert.Null(row.ActualDate);
        Assert.Null(row.Difference);
        Assert.False(row.WithdrawalCreated);
        Assert.DoesNotContain(
            (await client.ListTransactionsAsync(settled.Account.MoneyAssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);
        Assert.Equal(1_000m, await client.GetCashBalanceAsync(settled.Account.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Undo_AfterManualDelete_ClearsSettlement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settled = await client.CreateOneOffExpenseWithdrawalAsync(cancellationToken);
        var deleted = await client.DeleteAsync(TransactionUri(settled.Account.MoneyAssetId, settled.TransactionId), cancellationToken);
        Assert.True(deleted.IsSuccessStatusCode, $"delete returned {(int)deleted.StatusCode}.");

        var response = await client.DeleteAsync(OneOffExpenseSettlementUri(settled.ExpenseId), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var row = (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(settled.ExpenseId);
        Assert.Equal("Pending", row.Status);
        Assert.Null(row.ActualAmount);
        Assert.Equal(1_000m, await client.GetCashBalanceAsync(settled.Account.AccountId, cancellationToken));
    }

    [Fact]
    public async Task Undo_NotSettled_Returns404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(PlanningTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var expense = await client.CreateOneOffExpenseAsync(cancellationToken, "Wakacje", 100m, new DateOnly(2026, 10, 10));

        var response = await client.DeleteAsync(OneOffExpenseSettlementUri(expense), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.NotFound, OneOffExpenseSettlementNotFoundErrorCode, cancellationToken);
        Assert.Equal("Pending", (await client.GetPlanningMonthAsync("2026-10", cancellationToken)).Expenses.OneOff(expense).Status);
    }
}
