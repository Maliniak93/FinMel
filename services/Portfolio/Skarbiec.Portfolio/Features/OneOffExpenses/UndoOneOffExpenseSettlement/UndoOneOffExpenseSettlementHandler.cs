using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features.OneOffExpenses.UndoOneOffExpenseSettlement;

public sealed class UndoOneOffExpenseSettlementHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var expense = await dbContext.OneOffExpenses.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (expense is null)
        {
            return OneOffExpenseErrors.NotFound(id);
        }

        if (!expense.IsSettled)
        {
            return OneOffExpenseErrors.NotSettled(id);
        }

        Asset? money = null;
        if (expense.TransactionId is { } withdrawalId)
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

        expense.ActualAmount = null;
        expense.ActualDate = null;
        expense.ActualAccountId = null;
        expense.FxRateToPln = null;
        expense.TransactionId = null;

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
