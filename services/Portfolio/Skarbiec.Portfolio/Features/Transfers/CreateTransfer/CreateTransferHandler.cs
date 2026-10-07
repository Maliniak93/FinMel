using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Transfers.CreateTransfer;

public sealed class CreateTransferHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IFxRateLookupClient fxRateLookupClient,
    TimeProvider timeProvider)
{
    public async Task<Result<CreateTransferResponse>> HandleAsync(CreateTransferRequest request, CancellationToken cancellationToken)
    {
        if (request.Date > WarsawCalendar.Today(timeProvider))
        {
            return TransferErrors.DateInFuture;
        }

        // Both through the tenancy filter: a stranger's id gets the same 400 as any unsuitable asset, so nothing leaks.
        var source = await dbContext.Assets.FirstOrDefaultAsync(a => a.Id == request.SourceAssetId, cancellationToken);
        var target = await dbContext.Assets.FirstOrDefaultAsync(a => a.Id == request.TargetAssetId, cancellationToken);

        if (source is null
            || target is null
            || source.Id == target.Id
            || !TransferRoutes.IsCreatable(source.AssetClass, target.AssetClass)
            || source.Currency != target.Currency)
        {
            return TransferErrors.InvalidCounterpart;
        }

        // Both ids come from the body, so both sides get the read-only check.
        if (await dbContext.ReadOnlyErrorAsync(source, cancellationToken) is { } sourceReadOnly)
        {
            return sourceReadOnly;
        }

        if (await dbContext.ReadOnlyErrorAsync(target, cancellationToken) is { } targetReadOnly)
        {
            return targetReadOnly;
        }

        var (outLeg, inLeg) = TransferLegs.Create(source.Id, target.Id, request.Amount, request.Date);

        // The source's whole history is replayed with its Out leg, so its balance must cover it everywhere.
        var sourceQuantity = TransactionQuantityCalculator.Recompute(
            [.. await LoadHistoryAsync(source.Id, cancellationToken), outLeg], _ => TransferErrors.InsufficientFunds);
        if (sourceQuantity.IsFailure)
        {
            return sourceQuantity.Error;
        }

        var targetQuantity = TransactionQuantityCalculator.Recompute(
            [.. await LoadHistoryAsync(target.Id, cancellationToken), inLeg]);
        if (targetQuantity.IsFailure)
        {
            return targetQuantity.Error;
        }

        // Last check before staging: both legs freeze the date's one PLN rate, and MarketData being down leaves nothing half-written.
        var fxRateToPln = await fxRateLookupClient.ResolveFxRateToPlnAsync(source.Currency, request.Date, cancellationToken);
        if (fxRateToPln.IsFailure)
        {
            return fxRateToPln.Error;
        }

        outLeg.FxRateToPln = fxRateToPln.Value;
        inLeg.FxRateToPln = fxRateToPln.Value;
        source.Quantity = sourceQuantity.Value;
        target.Quantity = targetQuantity.Value;
        dbContext.Transactions.Add(outLeg);
        dbContext.Transactions.Add(inLeg);

        // Both publish before the one SaveChangesAsync, so the outbox rows commit with both legs.
        await positionEventPublisher.PublishChangedAsync(source, cancellationToken);
        await positionEventPublisher.PublishChangedAsync(target, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A write racing on either asset moved its xmin: nothing is saved, the user retries.
            return TransactionErrors.ConcurrentModification();
        }

        return new CreateTransferResponse { TransferId = outLeg.TransferId!.Value };
    }

    private Task<List<Transaction>> LoadHistoryAsync(Guid assetId, CancellationToken cancellationToken)
        => dbContext.Transactions.AsNoTracking().Where(t => t.AssetId == assetId).ToListAsync(cancellationToken);
}
