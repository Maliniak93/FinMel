using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.AddAsset;

public sealed class AddAssetHandler(PortfolioDbContext dbContext, IPublishEndpoint publishEndpoint, IInstrumentLookupClient instrumentLookupClient)
{
    public async Task<Result<AssetResponse>> HandleAsync(Guid portfolioId, AddAssetRequest request, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.FirstOrDefaultAsync(p => p.Id == portfolioId, cancellationToken);
        if (portfolio is null)
        {
            return PortfolioErrors.NotFound(portfolioId);
        }

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            PortfolioId = portfolioId,
            AssetClass = request.AssetClass,
            Name = request.Name,
            Currency = request.Currency,
            Quantity = request.Quantity,
        };

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
        }

        dbContext.Assets.Add(asset);
        portfolio.AssetCount++;

        // Published before SaveChangesAsync since the interceptor only stamps Asset.UserId during
        // that call (ADR-006) — dbContext.CurrentUserId is available immediately (ADR-012: publish
        // inside the same SaveChanges as the business write).
        await publishEndpoint.Publish(new AssetChanged
        {
            AssetId = asset.Id,
            PortfolioId = portfolioId,
            UserId = dbContext.CurrentUserId,
            Kind = AssetChangeKind.Created
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return asset.ToResponse();
    }
}
