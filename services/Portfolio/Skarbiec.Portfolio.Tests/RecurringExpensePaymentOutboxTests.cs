using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.RecurringExpenses.CreateRecurringExpense;
using Skarbiec.Portfolio.Features.RecurringExpenses.Payments.SettleExpensePayment;
using Skarbiec.Portfolio.Features.RecurringExpenses.Payments.UndoExpensePayment;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class RecurringExpensePaymentOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    private const string Month = "2026-01";

    [Fact]
    public async Task Settle_PublishesPositionChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var first = await CreateBankAccountAsync(scope.ServiceProvider, "mBank ROR", "PLN", 0m, cancellationToken, balance: 1_000m);
        var second = await CreateBankAccountAsync(scope.ServiceProvider, "Konto firmowe", "PLN", 0m, cancellationToken, balance: 1_000m);
        var created = await scope.ServiceProvider.GetRequiredService<CreateRecurringExpenseHandler>().HandleAsync(
            new CreateRecurringExpenseRequest
            {
                Name = "Czynsz",
                Amount = 100m,
                StartDate = new DateOnly(2026, 1, 1),
                IntervalMonths = 1,
                DayKind = RecurrenceDayKind.DayOfMonth,
                DayOfMonth = 10,
                Shift = BusinessDayShift.None
            },
            cancellationToken);
        Assert.True(created.IsSuccess);
        var expenseId = created.Value.Id;
        var settle = scope.ServiceProvider.GetRequiredService<SettleExpensePaymentHandler>();
        var published = await CountPositionEventsAsync(cancellationToken);
        SaveChanges.Reset();

        var settled = await settle.HandleAsync(expenseId, Month, Request(first.Id, 90m, new DateOnly(2026, 1, 10)), cancellationToken);

        Assert.True(settled.IsSuccess, settled.IsFailure ? settled.Error.Code : null);
        Assert.Equal(1, SaveChanges.Count);
        Assert.True(await dbContext.Set<RecurringExpenseOccurrence>()
            .AnyAsync(o => o.RecurringExpenseId == expenseId && o.ActualAmount == 90m, cancellationToken));
        var settleEvents = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(published).ToList();
        var settleEvent = Assert.Single(settleEvents);
        Assert.Equal(first.CashAssetId, settleEvent.AssetId);
        Assert.Equal(910m, settleEvent.Quantity);
        published += settleEvents.Count;
        SaveChanges.Reset();

        var moved = await settle.HandleAsync(expenseId, Month, Request(second.Id, 80m, new DateOnly(2026, 1, 12)), cancellationToken);

        Assert.True(moved.IsSuccess, moved.IsFailure ? moved.Error.Code : null);
        Assert.Equal(1, SaveChanges.Count);
        var moveEvents = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(published).ToList();
        Assert.Equal(2, moveEvents.Count);
        Assert.Equal(1_000m, Assert.Single(moveEvents, e => e.AssetId == first.CashAssetId).Quantity);
        Assert.Equal(920m, Assert.Single(moveEvents, e => e.AssetId == second.CashAssetId).Quantity);
        published += moveEvents.Count;
        SaveChanges.Reset();

        var undone = await scope.ServiceProvider.GetRequiredService<UndoExpensePaymentHandler>()
            .HandleAsync(expenseId, Month, cancellationToken);

        Assert.True(undone.IsSuccess, undone.IsFailure ? undone.Error.Code : null);
        Assert.Equal(1, SaveChanges.Count);
        Assert.False(await dbContext.Set<RecurringExpenseOccurrence>().AnyAsync(o => o.RecurringExpenseId == expenseId, cancellationToken));
        var undoEvent = Assert.Single((await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(published));
        Assert.Equal(second.CashAssetId, undoEvent.AssetId);
        Assert.Equal(1_000m, undoEvent.Quantity);
    }

    private static SettleExpensePaymentRequest Request(Guid accountId, decimal amount, DateOnly date) => new()
    {
        ActualAmount = amount,
        ActualDate = date,
        AccountId = accountId,
        CreateWithdrawal = true
    };
}
