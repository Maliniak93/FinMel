using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Bonds.UndoBondInterestSettlement;

public sealed class UndoBondInterestSettlementHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result> HandleAsync(Guid portfolioId, Guid assetId, Guid settlementId, CancellationToken cancellationToken)
    {
        // Tenancy-scoped lookups first, so a stranger never learns the portfolio's archived state.
        var bond = await dbContext.Assets.FirstOrDefaultAsync(
            a => a.Id == assetId && a.PortfolioId == portfolioId && a.AssetClass == AssetClass.Bond, cancellationToken);

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

        Asset? destination = null;
        if (settlement.TransferId is { } transferId)
        {
            var legs = await dbContext.Transactions.Where(t => t.TransferId == transferId).ToListAsync(cancellationToken);
            var inLeg = legs.SingleOrDefault(t => t.Type == TransactionType.Deposit);

            // Removing the Cash detached the bond's leg, which can then no longer be told apart from the transfer.
            if (legs.Count != 2 || inLeg is null)
            {
                return BondErrors.SettlementTransferDetached;
            }

            destination = await dbContext.Assets.FirstAsync(a => a.Id == inLeg.AssetId, cancellationToken);
            if (await dbContext.ReadOnlyErrorAsync(destination, cancellationToken) is { } destinationReadOnly)
            {
                return destinationReadOnly;
            }

            removed.AddRange(legs);
        }

        // Recompute each side without its removed rows before removing anything, so a later Withdraw that spent the coupon fails like DeleteTransfer.
        var bondQuantity = await RecomputeWithoutAsync(bond.Id, removed, cancellationToken);
        if (bondQuantity.IsFailure)
        {
            return bondQuantity.Error;
        }

        decimal? destinationQuantity = null;
        if (destination is not null)
        {
            var recomputed = await RecomputeWithoutAsync(destination.Id, removed, cancellationToken);
            if (recomputed.IsFailure)
            {
                return recomputed.Error;
            }

            destinationQuantity = recomputed.Value;
        }

        dbContext.Transactions.RemoveRange(removed);
        dbContext.BondInterestSettlements.Remove(settlement);
        bond.Quantity = bondQuantity.Value;

        // Published before SaveChangesAsync so the outbox rows commit with the removal.
        await positionEventPublisher.PublishChangedAsync(bond, cancellationToken);

        if (destination is not null && destinationQuantity is { } quantity)
        {
            destination.Quantity = quantity;
            await positionEventPublisher.PublishChangedAsync(destination, cancellationToken);
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
        Guid assetId, IReadOnlyCollection<Transaction> removed, CancellationToken cancellationToken)
    {
        var removedIds = removed.Select(t => t.Id).ToList();
        var remaining = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AssetId == assetId && !removedIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        return TransactionQuantityCalculator.Recompute(remaining, TransactionErrors.MutationBreaksHistory);
    }
}
