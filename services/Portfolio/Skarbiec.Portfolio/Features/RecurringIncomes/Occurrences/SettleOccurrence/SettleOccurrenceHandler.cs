using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences.SettleOccurrence;

public sealed class SettleOccurrenceHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(
        Guid id, string month, SettleOccurrenceRequest request, CancellationToken cancellationToken)
    {
        var parsed = OccurrenceRules.ParseMonth(month);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var nominal = parsed.Value;

        var income = await dbContext.RecurringIncomes
            .Include(i => i.Versions)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (income is null)
        {
            return RecurringIncomeErrors.NotFound(id);
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

        var stored = await dbContext.RecurringIncomeOccurrences
            .FirstOrDefaultAsync(o => o.RecurringIncomeId == id && o.NominalMonth == nominal.FirstDay, cancellationToken);
        var occurrence = stored ?? income.Freeze(nominal, OccurrenceStatus.Settled);
        if (occurrence is null)
        {
            return OccurrenceErrors.NotFound(id, month);
        }

        var plannedCurrency = await dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == occurrence.PlannedAccountId)
            .Select(a => a.Currency)
            .FirstAsync(cancellationToken);
        if (account.Currency != plannedCurrency)
        {
            return OccurrenceErrors.CurrencyMismatch(plannedCurrency, account.Currency);
        }

        if (account.IsArchived && account.Id != stored?.AccountId)
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
            occurrence.TransactionId, account.Id, request.CreateDeposit, request.ActualAmount, request.ActualDate, fxRateToPln, cancellationToken);
        if (synced.IsFailure)
        {
            return synced.Error;
        }

        occurrence.TransactionId = synced.Value.TransactionId;
        occurrence.Status = OccurrenceStatus.Settled;
        occurrence.ActualAmount = request.ActualAmount;
        occurrence.ActualDate = request.ActualDate;
        occurrence.AccountId = account.Id;
        occurrence.FxRateToPln = fxRateToPln;

        if (stored is null)
        {
            dbContext.RecurringIncomeOccurrences.Add(occurrence);
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
