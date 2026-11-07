using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.OneOffExpenses.SettleOneOffExpense;

public sealed class SettleOneOffExpenseHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid id, SettleOneOffExpenseRequest request, CancellationToken cancellationToken)
    {
        var expense = await dbContext.OneOffExpenses.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (expense is null)
        {
            return OneOffExpenseErrors.NotFound(id);
        }

        var account = await dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId, cancellationToken);
        if (account is null)
        {
            return AccountErrors.NotFound(request.AccountId);
        }

        var today = WarsawCalendar.Today(timeProvider);
        if (request.ActualDate > today)
        {
            return OccurrenceErrors.FutureDate(today);
        }

        if (account.IsArchived && account.Id != expense.ActualAccountId)
        {
            return AccountErrors.Archived(account.Id);
        }

        var rate = await fxRateLookupClient.ResolveFxRateToPlnAsync(account.Currency, request.ActualDate, cancellationToken);
        if (rate.IsFailure)
        {
            return rate.Error;
        }

        if (rate.Value is not { } fxRateToPln)
        {
            return TransactionErrors.FxRateMissing(account.Currency, request.ActualDate);
        }

        var synced = await dbContext.SyncExpenseWithdrawalAsync(
            expense.TransactionId, account.Id, request.CreateWithdrawal, request.ActualAmount, request.ActualDate, fxRateToPln, cancellationToken);
        if (synced.IsFailure)
        {
            return synced.Error;
        }

        expense.TransactionId = synced.Value.TransactionId;
        expense.ActualAmount = request.ActualAmount;
        expense.ActualDate = request.ActualDate;
        expense.ActualAccountId = account.Id;
        expense.FxRateToPln = fxRateToPln;
        if (request.RememberAccount)
        {
            expense.AccountId = account.Id;
        }

        foreach (var money in synced.Value.Touched)
        {
            await positionEventPublisher.PublishChangedAsync(money, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionErrors.ConcurrentModification();
        }

        return Result.Success();
    }
}
