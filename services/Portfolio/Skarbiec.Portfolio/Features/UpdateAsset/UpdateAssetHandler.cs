using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.UpdateAsset;

public sealed class UpdateAssetHandler(
    PortfolioDbContext dbContext, PositionEventPublisher positionEventPublisher, IInstrumentLookupClient instrumentLookupClient)
{
    public async Task<Result<AssetResponse>> HandleAsync(
        Guid portfolioId, Guid assetId, UpdateAssetRequest request, CancellationToken cancellationToken)
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

        // Each transaction's frozen PLN rate belongs to the asset's currency (ADR-026), so the
        // currency locks once there is one.
        if (!string.Equals(request.Currency, asset.Currency, StringComparison.Ordinal)
            && await dbContext.Transactions.AnyAsync(t => t.AssetId == assetId, cancellationToken))
        {
            return AssetErrors.CurrencyLockedByTransactions;
        }

        // Switching modes is allowed (T2.9, extended to three modes by M1.4) — transactions are
        // untouched either way (this handler never writes to the Transaction table), only the
        // valuation fields below move.
        if (request.InstrumentId is { } instrumentId)
        {
            var lookupStatus = await instrumentLookupClient.CheckAsync(instrumentId, cancellationToken);
            if (lookupStatus == InstrumentLookupStatus.NotFound)
            {
                return AssetErrors.InstrumentNotFound(instrumentId);
            }

            if (lookupStatus == InstrumentLookupStatus.Unavailable)
            {
                return AssetErrors.MarketDataUnavailable;
            }

            asset.InstrumentId = instrumentId;
            asset.ManualValueAmount = null;
            asset.ManualValueDate = null;
            asset.ValuationMode = AssetValuationMode.Market;
        }
        else if (request.ManualValue is { } manualAmount)
        {
            var manualValue = Money.Create(manualAmount, request.Currency);
            if (manualValue.IsFailure)
            {
                return manualValue.Error;
            }

            asset.ManualValueAmount = manualValue.Value.Amount;
            asset.ManualValueDate = request.ManualValueDate;
            asset.InstrumentId = null;
            asset.ValuationMode = AssetValuationMode.Manual;
        }
        else
        {
            // Currency-valued (M1.4): neither field — UpdateAssetRequest.Validate already confirmed
            // this class supports it. Clear both other modes' fields so switching into this mode from
            // Market/Manual doesn't leave stale data behind.
            asset.InstrumentId = null;
            asset.ManualValueAmount = null;
            asset.ManualValueDate = null;
            asset.ValuationMode = AssetValuationMode.CurrencyValued;
        }

        asset.AssetClass = request.AssetClass;
        asset.Name = request.Name;
        asset.Currency = request.Currency;
        // M1.5: no Asset.Quantity write here, deliberately — UpdateAssetRequest has no Quantity field
        // (see its <remarks>). Quantity only ever moves through TransactionQuantityCalculator.Recompute
        // (ADR-009), driven by RecordTransaction/UpdateTransaction/DeleteTransaction and AddAsset's own
        // optional initial transaction — never a second, parallel path here.

        // Carries the post-update state — mode, instrument, currency and manual value as they stand
        // after the assignments above (spec-02 AC-2).
        await positionEventPublisher.PublishChangedAsync(asset, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        // This slice never writes transactions, so the count is the same before and after the save.
        var transactionCount = await dbContext.Transactions.CountAsync(t => t.AssetId == assetId, cancellationToken);

        return asset.ToResponse(transactionCount);
    }
}
