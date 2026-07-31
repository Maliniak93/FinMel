using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.DeleteTransaction;

public sealed class DeleteTransactionHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid portfolioId, Guid assetId, Guid id, CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId && a.PortfolioId == portfolioId, cancellationToken);

        if (asset is null)
        {
            return AssetErrors.NotFound(assetId);
        }

        var transaction = await dbContext.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.AssetId == assetId, cancellationToken);

        if (transaction is null)
        {
            return TransactionErrors.NotFound(id);
        }

        // Recompute over what remains without the deleted transaction, without removing it from
        // the change tracker yet — a rejected delete must leave the database untouched (AC).
        var remainingTransactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId && t.Id != id)
            .ToListAsync(cancellationToken);

        var recomputed = TransactionQuantityCalculator.Recompute(remainingTransactions, TransactionErrors.MutationBreaksHistory);
        if (recomputed.IsFailure)
        {
            return recomputed.Error;
        }

        asset.Quantity = recomputed.Value;
        asset.TransactionCount--;
        dbContext.Transactions.Remove(transaction);

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
