using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.UpdateTransaction;

public sealed class UpdateTransactionHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<TransactionResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, Guid id, UpdateTransactionRequest request, CancellationToken cancellationToken)
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

        var unitPrice = Money.Create(request.UnitPrice, asset.Currency);
        if (unitPrice.IsFailure)
        {
            return unitPrice.Error;
        }

        var fee = Money.Create(request.Fee, asset.Currency);
        if (fee.IsFailure)
        {
            return fee.Error;
        }

        // Recompute over the rest of the history plus the edited candidate, without touching the
        // tracked `transaction` yet — a rejected edit must leave the database untouched (AC).
        var otherTransactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId && t.Id != id)
            .ToListAsync(cancellationToken);

        var candidate = new Transaction
        {
            Id = transaction.Id,
            AssetId = assetId,
            Type = request.Type,
            Quantity = request.Quantity,
            UnitPriceAmount = unitPrice.Value.Amount,
            FeeAmount = fee.Value.Amount,
            Date = request.Date
        };

        var recomputed = TransactionQuantityCalculator.Recompute([.. otherTransactions, candidate], TransactionErrors.MutationBreaksHistory);
        if (recomputed.IsFailure)
        {
            return recomputed.Error;
        }

        transaction.Type = candidate.Type;
        transaction.Quantity = candidate.Quantity;
        transaction.UnitPriceAmount = candidate.UnitPriceAmount;
        transaction.FeeAmount = candidate.FeeAmount;
        transaction.Date = candidate.Date;
        asset.Quantity = recomputed.Value;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionErrors.ConcurrentModification();
        }

        return transaction.ToResponse();
    }
}
