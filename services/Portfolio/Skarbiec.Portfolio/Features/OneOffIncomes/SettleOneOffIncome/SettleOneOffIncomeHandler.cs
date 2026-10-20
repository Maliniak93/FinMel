using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.OneOffIncomes.SettleOneOffIncome;

public sealed class SettleOneOffIncomeHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid id, SettleOneOffIncomeRequest request, CancellationToken cancellationToken)
    {
        var income = await dbContext.OneOffIncomes.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (income is null)
        {
            return OneOffIncomeErrors.NotFound(id);
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

        var plannedCurrency = await dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == income.AccountId)
            .Select(a => a.Currency)
            .FirstAsync(cancellationToken);
        if (account.Currency != plannedCurrency)
        {
            return OneOffIncomeErrors.CurrencyMismatch(plannedCurrency, account.Currency);
        }

        if (account.IsArchived && account.Id != income.ActualAccountId)
        {
            return AccountErrors.Archived(account.Id);
        }

        var rate = await fxRateLookupClient.ResolveFxRateToPlnAsync(plannedCurrency, request.ActualDate, cancellationToken);
        if (rate.IsFailure)
        {
            return rate.Error;
        }

        if (rate.Value is not { } fxRateToPln)
        {
            return TransactionErrors.FxRateMissing(plannedCurrency, request.ActualDate);
        }

        var synced = await dbContext.SyncIncomeDepositAsync(
            income.TransactionId, account.Id, request.CreateDeposit, request.ActualAmount, request.ActualDate, fxRateToPln, cancellationToken);
        if (synced.IsFailure)
        {
            return synced.Error;
        }

        income.TransactionId = synced.Value.TransactionId;
        income.ActualAmount = request.ActualAmount;
        income.ActualDate = request.ActualDate;
        income.ActualAccountId = account.Id;
        income.FxRateToPln = fxRateToPln;

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
