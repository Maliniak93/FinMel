using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.UndoSavingsInterestSettlement;

/// <summary>
/// Undoes a savings account's latest interest settlement (savings-interest-settlement): deletes it and
/// its credit, so the month becomes the due one again. Editing a settlement means undoing and settling it again.
/// </summary>
public sealed class UndoSavingsInterestSettlementHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result> HandleAsync(Guid portfolioId, Guid assetId, Guid settlementId, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookups first: another user's account or settlement is 404, and never learns
        // the portfolio's archived state.
        var asset = await dbContext.Assets.FirstOrDefaultAsync(
            a => a.Id == assetId && a.PortfolioId == portfolioId && a.AssetClass == AssetClass.Savings, cancellationToken);

        if (asset is null)
        {
            return SavingsAccountErrors.NotFound(assetId);
        }

        var settlement = await dbContext.SavingsInterestSettlements
            .FirstOrDefaultAsync(s => s.Id == settlementId && s.AssetId == assetId, cancellationToken);

        if (settlement is null)
        {
            return SavingsAccountErrors.SettlementNotFound(settlementId);
        }

        if (await dbContext.ReadOnlyErrorAsync(asset, cancellationToken) is { } readOnly)
        {
            return readOnly;
        }

        if (await dbContext.SavingsInterestSettlements.AnyAsync(
                s => s.AssetId == assetId && s.PeriodEnd > settlement.PeriodEnd, cancellationToken))
        {
            return SavingsAccountErrors.SettlementNotLatest;
        }

        if (settlement.TransactionId is { } creditId)
        {
            // Recompute over the history without the credit before removing anything — a later Withdraw
            // that spent it fails exactly as DeleteTransaction would, and nothing changes.
            var remainingTransactions = await dbContext.Transactions
                .AsNoTracking()
                .Where(t => t.AssetId == assetId && t.Id != creditId)
                .ToListAsync(cancellationToken);

            var recomputed = TransactionQuantityCalculator.Recompute(remainingTransactions, TransactionErrors.MutationBreaksHistory);
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            var credit = await dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == creditId, cancellationToken);
            if (credit is not null)
            {
                dbContext.Transactions.Remove(credit);
            }

            asset.Quantity = recomputed.Value;
        }

        dbContext.SavingsInterestSettlements.Remove(settlement);

        // Published before SaveChangesAsync so the outbox row commits with the removal (ADR-012).
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
