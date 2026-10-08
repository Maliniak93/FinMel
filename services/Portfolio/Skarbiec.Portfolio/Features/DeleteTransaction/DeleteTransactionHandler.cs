using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.SavingsAccounts;
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

        if (await dbContext.ReadOnlyErrorAsync(asset, cancellationToken) is { } readOnly)
        {
            return readOnly;
        }

        // A term deposit's only transaction is its opening one, rewritten by UpdateDeposit.
        if (asset.AssetClass == AssetClass.Deposit)
        {
            return DepositErrors.TransactionsManaged;
        }

        // A treasury bond's only transaction is its opening one, rewritten by UpdateBond.
        if (asset.AssetClass == AssetClass.Bond)
        {
            return BondErrors.TransactionsManaged;
        }

        var transaction = await dbContext.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.AssetId == assetId, cancellationToken);

        if (transaction is null)
        {
            return TransactionErrors.NotFound(id);
        }

        // A transfer leg changes only through its transfer's entry point, so the two legs never drift apart.
        if (transaction.TransferId is not null)
        {
            // A stock or ETF trade's pair is deleted from the security side.
            return asset.AssetClass is AssetClass.Stock or AssetClass.Etf
                ? await DeleteTradeAsync(asset, transaction, cancellationToken)
                : TransferErrors.LegManaged;
        }

        // A savings interest credit goes only by undoing its settlement, so a settlement never loses its credit.
        if (asset.AssetClass == AssetClass.Savings
            && await dbContext.SavingsInterestSettlements.AnyAsync(s => s.TransactionId == id, cancellationToken))
        {
            return SavingsAccountErrors.InterestManaged;
        }

        // Recompute without the deleted transaction before removing it, so a rejected delete leaves the database untouched.
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

    private async Task<Result> DeleteTradeAsync(Asset asset, Transaction transaction, CancellationToken cancellationToken)
    {
        var cashLeg = await dbContext.Transactions
            .FirstOrDefaultAsync(t => t.TransferId == transaction.TransferId && t.Id != transaction.Id, cancellationToken);
        if (cashLeg is null)
        {
            return TransferErrors.LegManaged;
        }

        var cash = await dbContext.Assets.FirstAsync(a => a.Id == cashLeg.AssetId, cancellationToken);
        if (await dbContext.ReadOnlyErrorAsync(cash, cancellationToken) is { } cashReadOnly)
        {
            return cashReadOnly;
        }

        var securityQuantity = await RecomputeWithoutAsync(transaction, cancellationToken);
        if (securityQuantity.IsFailure)
        {
            return securityQuantity.Error;
        }

        var cashQuantity = await RecomputeWithoutAsync(cashLeg, cancellationToken);
        if (cashQuantity.IsFailure)
        {
            return cashQuantity.Error;
        }

        asset.Quantity = securityQuantity.Value;
        cash.Quantity = cashQuantity.Value;
        dbContext.Transactions.RemoveRange(transaction, cashLeg);

        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);
        await positionEventPublisher.PublishChangedAsync(cash, cancellationToken);

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

    private async Task<Result<decimal>> RecomputeWithoutAsync(Transaction leg, CancellationToken cancellationToken)
    {
        var remaining = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == leg.AssetId && t.Id != leg.Id)
            .ToListAsync(cancellationToken);

        return TransactionQuantityCalculator.Recompute(remaining, TransactionErrors.MutationBreaksHistory);
    }
}
