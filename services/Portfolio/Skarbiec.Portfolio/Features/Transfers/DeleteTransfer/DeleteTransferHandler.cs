using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Transfers.DeleteTransfer;

/// <summary>
/// Deletes a manual transfer as a whole (savings-cash-transfers): both legs go, both assets are
/// recomputed and both publish, in one save. A transfer is never edited — delete it and create it again.
/// </summary>
public sealed class DeleteTransferHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result> HandleAsync(Guid transferId, CancellationToken cancellationToken)
    {
        // Tenancy-filtered: a stranger's transfer is simply not found.
        var legs = await dbContext.Transactions
            .Where(t => t.TransferId == transferId)
            .ToListAsync(cancellationToken);

        // A transfer is exactly two legs; detach on removal clears the survivor's TransferId, so a
        // lone leg never carries one.
        var outLeg = legs.SingleOrDefault(t => t.Type == TransactionType.Withdraw);
        var inLeg = legs.SingleOrDefault(t => t.Type == TransactionType.Deposit);
        if (legs.Count != 2 || outLeg is null || inLeg is null)
        {
            return TransferErrors.NotFound(transferId);
        }

        var source = await dbContext.Assets.FirstAsync(a => a.Id == outLeg.AssetId, cancellationToken);
        var target = await dbContext.Assets.FirstAsync(a => a.Id == inLeg.AssetId, cancellationToken);

        // A deposit route belongs to its own entry point (asset-transfers-deposit-funding).
        if (!TransferRoutes.IsManual(source.AssetClass, target.AssetClass))
        {
            return TransferErrors.LegManaged;
        }

        if (await dbContext.ReadOnlyErrorAsync(source, cancellationToken) is { } sourceReadOnly)
        {
            return sourceReadOnly;
        }

        if (await dbContext.ReadOnlyErrorAsync(target, cancellationToken) is { } targetReadOnly)
        {
            return targetReadOnly;
        }

        // Recompute each side over what remains without its leg, before anything is removed from the
        // change tracker — a rejected delete must leave the database untouched. Removing the inflow
        // can break the target's later history, refused like any DeleteTransaction would be.
        var sourceQuantity = await RecomputeWithoutAsync(outLeg, cancellationToken);
        if (sourceQuantity.IsFailure)
        {
            return sourceQuantity.Error;
        }

        var targetQuantity = await RecomputeWithoutAsync(inLeg, cancellationToken);
        if (targetQuantity.IsFailure)
        {
            return targetQuantity.Error;
        }

        source.Quantity = sourceQuantity.Value;
        target.Quantity = targetQuantity.Value;
        dbContext.Transactions.RemoveRange(legs);

        // Both publish before the one SaveChangesAsync, so the outbox rows commit with the removal (ADR-012).
        await positionEventPublisher.PublishChangedAsync(source, cancellationToken);
        await positionEventPublisher.PublishChangedAsync(target, cancellationToken);

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
