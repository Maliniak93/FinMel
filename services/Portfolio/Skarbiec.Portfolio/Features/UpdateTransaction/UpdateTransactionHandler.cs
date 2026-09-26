using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.UpdateTransaction;

public sealed class UpdateTransactionHandler(
    PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher, IFxRateLookupClient fxRateLookupClient)
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

        if (await dbContext.IsPortfolioArchivedAsync(portfolioId, cancellationToken))
        {
            return PortfolioErrors.Archived(portfolioId);
        }

        // A term deposit's only transaction is its opening one, rewritten by UpdateDeposit (term-deposits).
        if (asset.AssetClass == AssetClass.Deposit)
        {
            return DepositErrors.TransactionsManaged;
        }

        var transaction = await dbContext.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.AssetId == assetId, cancellationToken);

        if (transaction is null)
        {
            return TransactionErrors.NotFound(id);
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
            Date = request.Date
        };

        var recomputed = TransactionQuantityCalculator.Recompute([.. otherTransactions, candidate], TransactionErrors.MutationBreaksHistory);
        if (recomputed.IsFailure)
        {
            return recomputed.Error;
        }

        // Re-resolved on every update, whatever changed, so the stored rate always matches the
        // current date (ADR-026) — and a rate that was unknown before can fill in here.
        var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(asset.Currency, request.Date, cancellationToken);
        if (fxRateToPln.IsFailure)
        {
            return fxRateToPln.Error;
        }

        transaction.Type = candidate.Type;
        transaction.Quantity = candidate.Quantity;
        transaction.UnitPriceAmount = candidate.UnitPriceAmount;
        transaction.FxRateToPln = fxRateToPln.Value;
        transaction.Date = candidate.Date;
        asset.Quantity = recomputed.Value;

        // Editing a transaction moves the quantity, which is the source of truth for the position
        // (ADR-009) — so it publishes, where before spec-02 it published nothing at all (AC-4).
        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TransactionErrors.ConcurrentModification();
        }

        return transaction.ToResponse(asset.Currency);
    }
}
