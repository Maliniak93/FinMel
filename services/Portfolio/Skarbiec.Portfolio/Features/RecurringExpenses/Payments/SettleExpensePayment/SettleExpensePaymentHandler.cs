using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.Payments.SettleExpensePayment;

public sealed class SettleExpensePaymentHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(
        Guid id, string month, SettleExpensePaymentRequest request, CancellationToken cancellationToken)
    {
        var parsed = OccurrenceRules.ParseMonth(month);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var nominal = parsed.Value;

        var expense = await dbContext.RecurringExpenses
            .Include(e => e.Versions)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (expense is null)
        {
            return RecurringExpenseErrors.NotFound(id);
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

        var stored = await dbContext.RecurringExpenseOccurrences
            .FirstOrDefaultAsync(o => o.RecurringExpenseId == id && o.NominalMonth == nominal.FirstDay, cancellationToken);
        var planned = expense.ComputedAt(nominal);
        if (stored is null && planned is null)
        {
            return RecurringExpenseErrors.PaymentNotFound(id, month);
        }

        if (account.IsArchived && account.Id != stored?.AccountId)
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
            stored?.TransactionId, account.Id, request.CreateWithdrawal, request.ActualAmount, request.ActualDate, fxRateToPln, cancellationToken);
        if (synced.IsFailure)
        {
            return synced.Error;
        }

        var occurrence = stored ?? new RecurringExpenseOccurrence
        {
            Id = Guid.NewGuid(),
            RecurringExpenseId = id,
            NominalMonth = nominal.FirstDay,
            PlannedDate = planned!.Value.Date,
            PlannedAmount = planned.Value.Amount
        };
        occurrence.TransactionId = synced.Value.TransactionId;
        occurrence.ActualAmount = request.ActualAmount;
        occurrence.ActualDate = request.ActualDate;
        occurrence.AccountId = account.Id;
        occurrence.FxRateToPln = fxRateToPln;

        if (stored is null)
        {
            dbContext.RecurringExpenseOccurrences.Add(occurrence);
        }

        if (request.RememberAccount)
        {
            RememberAccount(expense, nominal, account.Id);
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

    private void RememberAccount(RecurringExpense expense, YearMonth nominal, Guid accountId)
    {
        var target = expense.EditableFrom(nominal);
        if (target is null)
        {
            var inForce = expense.InForce(nominal);
            target = new RecurringExpenseVersion
            {
                Id = Guid.NewGuid(),
                RecurringExpenseId = expense.Id,
                EffectiveFrom = nominal.FirstDay,
                Amount = inForce.Amount,
                IntervalMonths = inForce.IntervalMonths,
                DayKind = inForce.DayKind,
                DayOfMonth = inForce.DayOfMonth,
                Shift = inForce.Shift
            };
            dbContext.RecurringExpenseVersions.Add(target);
        }

        target.AccountId = accountId;
    }
}
