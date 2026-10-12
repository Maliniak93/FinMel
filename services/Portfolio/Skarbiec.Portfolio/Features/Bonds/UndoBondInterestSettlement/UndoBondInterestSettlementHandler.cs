using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features.Bonds.UndoBondInterestSettlement;

public sealed class UndoBondInterestSettlementHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result> HandleAsync(Guid assetId, Guid settlementId, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookups first, so a stranger never learns an account's archived state.
        var bond = await dbContext.Assets.FirstOrDefaultAsync(
            a => a.Id == assetId && a.AssetClass == AssetClass.Bond, cancellationToken);

        if (bond is null)
        {
            return BondErrors.NotFound(assetId);
        }

        var settlement = await dbContext.BondInterestSettlements
            .FirstOrDefaultAsync(s => s.Id == settlementId && s.AssetId == assetId, cancellationToken);

        if (settlement is null)
        {
            return BondErrors.SettlementNotFound(settlementId);
        }

        if (await dbContext.ReadOnlyErrorAsync(bond, cancellationToken) is { } readOnly)
        {
            return readOnly;
        }

        // The redemption was computed from every settlement and has no undo, so they stay fixed.
        if (await dbContext.BondRedemptions.AnyAsync(r => r.AssetId == assetId, cancellationToken))
        {
            return BondErrors.Redeemed;
        }

        if (await dbContext.BondInterestSettlements.AnyAsync(
                s => s.AssetId == assetId && s.PeriodIndex > settlement.PeriodIndex, cancellationToken))
        {
            return BondErrors.SettlementNotLatest;
        }

        var removed = new List<Transaction>();
        if (settlement.CreditTransactionId is { } creditId
            && await dbContext.Transactions.FirstOrDefaultAsync(t => t.Id == creditId, cancellationToken) is { } credit)
        {
            removed.Add(credit);
        }

        Asset? cash = null;
        if (settlement.TransferId is { } transferId)
        {
            var legs = await dbContext.Transactions.Where(t => t.TransferId == transferId).ToListAsync(cancellationToken);
            cash = await dbContext.LoadAccountCashAsync(bond.AccountId!.Value, cancellationToken);
            removed.AddRange(legs);
        }

        // Recompute each side without its removed rows before removing anything, so a later Withdraw that spent the coupon fails like DeleteTransfer.
        var bondQuantity = await RecomputeWithoutAsync(bond.Id, removed, TransactionErrors.MutationBreaksHistory, cancellationToken);
        if (bondQuantity.IsFailure)
        {
            return bondQuantity.Error;
        }

        decimal? cashQuantity = null;
        if (cash is not null)
        {
            var recomputed = await RecomputeWithoutAsync(cash.Id, removed, _ => TransferErrors.InsufficientFunds, cancellationToken);
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            cashQuantity = recomputed.Value;
        }

        dbContext.Transactions.RemoveRange(removed);
        dbContext.BondInterestSettlements.Remove(settlement);
        bond.Quantity = bondQuantity.Value;

        // Published before SaveChangesAsync so the outbox rows commit with the removal.
        await positionEventPublisher.PublishChangedAsync(bond, cancellationToken);

        if (cash is not null && cashQuantity is { } quantity)
        {
            cash.Quantity = quantity;
            await positionEventPublisher.PublishChangedAsync(cash, cancellationToken);
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

    private async Task<Result<decimal>> RecomputeWithoutAsync(
        Guid assetId,
        IReadOnlyCollection<Transaction> removed,
        Func<TransactionType, Error> oversellError,
        CancellationToken cancellationToken)
    {
        var removedIds = removed.Select(t => t.Id).ToList();
        var remaining = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId && !removedIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        return TransactionQuantityCalculator.Recompute(remaining, oversellError);
    }
}
