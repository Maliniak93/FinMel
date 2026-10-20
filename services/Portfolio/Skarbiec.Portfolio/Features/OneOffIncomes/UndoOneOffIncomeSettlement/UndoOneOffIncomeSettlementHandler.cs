using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Features.OneOffIncomes.UndoOneOffIncomeSettlement;

public sealed class UndoOneOffIncomeSettlementHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var income = await dbContext.OneOffIncomes.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (income is null)
        {
            return OneOffIncomeErrors.NotFound(id);
        }

        if (!income.IsSettled)
        {
            return OneOffIncomeErrors.NotSettled(id);
        }

        Asset? money = null;
        if (income.TransactionId is { } depositId)
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

        income.ActualAmount = null;
        income.ActualDate = null;
        income.ActualAccountId = null;
        income.FxRateToPln = null;
        income.TransactionId = null;

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
