using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.Payments.UndoExpensePayment;

public sealed class UndoExpensePaymentHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result> HandleAsync(Guid id, string month, CancellationToken cancellationToken)
    {
        var parsed = OccurrenceRules.ParseMonth(month);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        if (!await dbContext.RecurringExpenses.AnyAsync(e => e.Id == id, cancellationToken))
        {
            return RecurringExpenseErrors.NotFound(id);
        }

        var stored = await dbContext.RecurringExpenseOccurrences
            .FirstOrDefaultAsync(o => o.RecurringExpenseId == id && o.NominalMonth == parsed.Value.FirstDay, cancellationToken);
        if (stored is null)
        {
            return RecurringExpenseErrors.PaymentNotFound(id, month);
        }

        Asset? money = null;
        if (stored.TransactionId is { } withdrawalId)
        {
            var withdrawal = await dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == withdrawalId, cancellationToken);
            if (withdrawal is not null)
            {
                money = await dbContext.Assets.FirstAsync(a => a.Id == withdrawal.AssetId, cancellationToken);
                if (await dbContext.ReadOnlyErrorAsync(money, cancellationToken) is { } readOnly)
                {
                    return readOnly;
                }

                var remaining = await dbContext.Transactions
                    .AsNoTracking()
                    .Where(t => t.AssetId == money.Id && t.Id != withdrawalId)
                    .ToListAsync(cancellationToken);

                var recomputed = TransactionQuantityCalculator.Recompute(remaining, money.OversellError(TransactionErrors.MutationBreaksHistory));
                if (recomputed.IsFailure)
                {
                    return recomputed.Error;
                }

                money.Quantity = recomputed.Value;
                dbContext.Transactions.Remove(withdrawal);
            }
        }

        dbContext.RecurringExpenseOccurrences.Remove(stored);

        if (money is not null)
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
