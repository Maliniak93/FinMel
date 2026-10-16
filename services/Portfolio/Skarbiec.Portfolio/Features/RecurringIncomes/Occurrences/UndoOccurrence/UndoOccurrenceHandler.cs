using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences.UndoOccurrence;

public sealed class UndoOccurrenceHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result> HandleAsync(Guid id, string month, CancellationToken cancellationToken)
    {
        var parsed = OccurrenceRules.ParseMonth(month);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        if (!await dbContext.RecurringIncomes.AnyAsync(i => i.Id == id, cancellationToken))
        {
            return RecurringIncomeErrors.NotFound(id);
        }

        var stored = await dbContext.RecurringIncomeOccurrences
            .FirstOrDefaultAsync(o => o.RecurringIncomeId == id && o.NominalMonth == parsed.Value.FirstDay, cancellationToken);
        if (stored is null)
        {
            return OccurrenceErrors.NotFound(id, month);
        }

        Asset? money = null;
        if (stored.TransactionId is { } depositId)
        {
            var deposit = await dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == depositId, cancellationToken);
            if (deposit is not null)
            {
                money = await dbContext.Assets.FirstAsync(a => a.Id == deposit.AssetId, cancellationToken);
                if (await dbContext.ReadOnlyErrorAsync(money, cancellationToken) is { } readOnly)
                {
                    return readOnly;
                }

                // Recompute without the deposit before removing anything, so a later Withdraw that spent it fails like DeleteTransaction.
                var remaining = await dbContext.Transactions
                    .AsNoTracking()
                    .Where(t => t.AssetId == money.Id && t.Id != depositId)
                    .ToListAsync(cancellationToken);

                var recomputed = TransactionQuantityCalculator.Recompute(remaining, money.OversellError(TransactionErrors.MutationBreaksHistory));
                if (recomputed.IsFailure)
                {
                    return recomputed.Error;
                }

                money.Quantity = recomputed.Value;
                dbContext.Transactions.Remove(deposit);
            }
        }

        dbContext.RecurringIncomeOccurrences.Remove(stored);

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
