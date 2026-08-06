using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.UpdateAsset;

public sealed class UpdateAssetHandler(PortfolioDbContext dbContext, IPublishEndpoint publishEndpoint, IInstrumentLookupClient instrumentLookupClient)
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

        // Switching modes is allowed (T2.9) — transactions are untouched either way (this handler
        // never writes to the Transaction table), only the valuation fields below move.
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
        }
        else
        {
            var manualValue = Money.Create(request.ManualValue!.Value, request.Currency);
            if (manualValue.IsFailure)
            {
                return manualValue.Error;
            }

            asset.ManualValueAmount = manualValue.Value.Amount;
            asset.ManualValueDate = request.ManualValueDate;
            asset.InstrumentId = null;
        }

        asset.AssetClass = request.AssetClass;
        asset.Name = request.Name;
        asset.Currency = request.Currency;
        asset.Quantity = request.Quantity;

        await publishEndpoint.Publish(new AssetChanged
        {
            AssetId = asset.Id,
            PortfolioId = portfolioId,
            UserId = dbContext.CurrentUserId,
            Kind = AssetChangeKind.Updated
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return asset.ToResponse();
    }
}
