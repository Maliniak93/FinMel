using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.DeleteTransaction;

public sealed class DeleteTransactionHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result> HandleAsync(Guid portfolioId, Guid assetId, Guid id, CancellationToken cancellationToken)
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

        // A transfer leg changes only through its transfer's entry point (asset-transfers-deposit-funding),
        // so the two legs never drift apart.
        if (transaction.TransferId is not null)
        {
            return TransferErrors.LegManaged;
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
        dbContext.Transactions.Remove(transaction);

        // Deleting a transaction moves the quantity, which is the source of truth for the position
        // (ADR-009) — so it publishes, where before spec-02 it published nothing at all (AC-5).
        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

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
