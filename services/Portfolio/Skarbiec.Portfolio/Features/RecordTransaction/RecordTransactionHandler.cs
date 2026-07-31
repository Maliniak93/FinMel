using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.RecordTransaction;

public sealed class RecordTransactionHandler(PortfolioDbContext dbContext)
{
    public async Task<Result<TransactionResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, RecordTransactionRequest request, CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId && a.PortfolioId == portfolioId, cancellationToken);

        if (asset is null)
        {
            return AssetErrors.NotFound(assetId);
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

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            Type = request.Type,
            Quantity = request.Quantity,
            UnitPriceAmount = unitPrice.Value.Amount,
            FeeAmount = fee.Value.Amount,
            Date = request.Date
        };

        // Recompute over the full history (existing + this candidate) rather than incrementing
        // in place — the single code path shared with T1.4's edit/delete (ADR-009).
        var existingTransactions = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId)
            .ToListAsync(cancellationToken);

        var recomputed = TransactionQuantityCalculator.Recompute([.. existingTransactions, transaction]);
        if (recomputed.IsFailure)
        {
            return recomputed.Error;
        }

        asset.Quantity = recomputed.Value;
        asset.TransactionCount++;
        dbContext.Transactions.Add(transaction);

        await dbContext.SaveChangesAsync(cancellationToken);

        return transaction.ToResponse();
    }
}
