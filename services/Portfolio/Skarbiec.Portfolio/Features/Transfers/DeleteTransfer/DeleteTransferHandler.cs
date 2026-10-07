using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Transfers.DeleteTransfer;

public sealed class DeleteTransferHandler(PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher)
{
    public async Task<Result> HandleAsync(Guid transferId, CancellationToken cancellationToken)
    {
        // Tenancy-filtered: a stranger's transfer is simply not found.
        var legs = await dbContext.Transactions
            .Where(t => t.TransferId == transferId)
            .ToListAsync(cancellationToken);

        // Detach on removal clears the survivor's TransferId, so a lone leg never carries one.
        var outLeg = legs.SingleOrDefault(t => TransferLegs.DirectionOf(t) == TransferDirection.Out);
        var inLeg = legs.SingleOrDefault(t => TransferLegs.DirectionOf(t) == TransferDirection.In);
        if (legs.Count != 2 || outLeg is null || inLeg is null)
        {
            return TransferErrors.NotFound(transferId);
        }

        var source = await dbContext.Assets.FirstAsync(a => a.Id == outLeg.AssetId, cancellationToken);
        var target = await dbContext.Assets.FirstAsync(a => a.Id == inLeg.AssetId, cancellationToken);

        // A deposit route belongs to its own entry point.
        if (!TransferRoutes.IsDeletable(source.AssetClass, target.AssetClass))
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

        // Recompute each side without its leg before removing anything, so a rejected delete leaves the database untouched.
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

        // Both publish before the one SaveChangesAsync, so the outbox rows commit with the removal.
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
