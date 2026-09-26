using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.RecordTransaction;

public sealed class RecordTransactionHandler(
    PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher, IFxRateLookupClient fxRateLookupClient)
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

        if (await dbContext.IsPortfolioArchivedAsync(portfolioId, cancellationToken))
        {
            return PortfolioErrors.Archived(portfolioId);
        }

        // A term deposit's transactions are system-managed by the deposit slices (term-deposits).
        if (asset.AssetClass == AssetClass.Deposit)
        {
            return DepositErrors.TransactionsManaged;
        }

        if (!AssetTransactionTypes.IsAllowed(asset.AssetClass, request.Type))
        {
            return TransactionErrors.TypeNotAllowedForClass(request.Type, asset.AssetClass);
        }

        var unitPrice = Money.Create(request.UnitPrice, asset.Currency);
        if (unitPrice.IsFailure)
        {
            return unitPrice.Error;
        }

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            Type = request.Type,
            Quantity = request.Quantity,
            UnitPriceAmount = unitPrice.Value.Amount,
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

        // Last check before the write: only a valid request on an active portfolio asks MarketData.
        var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(asset.Currency, request.Date, cancellationToken);
        if (fxRateToPln.IsFailure)
        {
            return fxRateToPln.Error;
        }

        transaction.FxRateToPln = fxRateToPln.Value;
        asset.Quantity = recomputed.Value;
        dbContext.Transactions.Add(transaction);

        // The position, not the transaction, is the fact consumers care about: the event carries the
        // recomputed quantity (spec-02 AC-3), so nobody has to replay the history to get it.
        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return transaction.ToResponse(asset.Currency);
    }
}
