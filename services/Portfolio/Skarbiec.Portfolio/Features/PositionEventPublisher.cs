using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features;

// One event's construction shared by every mutating slice, not a service layer.
public sealed class PositionEventPublisher(
    PortfolioDbContext dbContext, IPublishEndpoint publishEndpoint, TimeProvider timeProvider)
{
    public Task PublishCreatedAsync(Asset asset, PortfolioEntity portfolio, CancellationToken cancellationToken)
        => PublishAsync(asset, portfolio.IsArchived, cancellationToken);

    public async Task PublishChangedAsync(Asset asset, CancellationToken cancellationToken)
    {
        var portfolioIsArchived = await dbContext.Portfolios
            .AsNoTracking()
            .Where(p => p.Id == asset.PortfolioId)
            .Select(p => p.IsArchived)
            .FirstAsync(cancellationToken);

        asset.Version++;
        await PublishAsync(asset, portfolioIsArchived, cancellationToken);
    }

    public async Task PublishForEveryAssetAsync(PortfolioEntity portfolio, CancellationToken cancellationToken)
    {
        var assets = await dbContext.Assets
            .Where(a => a.PortfolioId == portfolio.Id)
            .ToListAsync(cancellationToken);

        foreach (var asset in assets)
        {
            asset.Version++;
            await PublishAsync(asset, portfolio.IsArchived, cancellationToken);
        }
    }

    private Task PublishAsync(Asset asset, bool portfolioIsArchived, CancellationToken cancellationToken)
        // The interceptor stamps Asset.UserId only during SaveChangesAsync, which has not run yet.
        => publishEndpoint.Publish(new AssetPositionChanged
        {
            AssetId = asset.Id,
            PortfolioId = asset.PortfolioId,
            UserId = dbContext.CurrentUserId,
            AssetClass = asset.AssetClass,
            ValuationMode = asset.ValuationMode,
            InstrumentId = asset.InstrumentId,
            Currency = asset.Currency,
            Quantity = asset.Quantity,
            ManualValueAmount = asset.ManualValueAmount,
            ManualValueDate = asset.ManualValueDate,
            PortfolioIsArchived = portfolioIsArchived,
            // The asset's own flag; the portfolio fan-out never changes it.
            IsArchived = asset.IsArchived,
            Version = asset.Version,
            OccurredAtUtc = timeProvider.GetUtcNow()
        }, cancellationToken);
}
