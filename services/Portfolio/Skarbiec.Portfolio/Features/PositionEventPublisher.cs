using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features;

// One event's construction shared by every mutating slice, not a service layer.
public sealed class PositionEventPublisher(
    PortfolioDbContext dbContext, IPublishEndpoint publishEndpoint, TimeProvider timeProvider)
{
    public async Task PublishCreatedAsync(Asset asset, PortfolioEntity portfolio, CancellationToken cancellationToken)
    {
        var firstDates = await FirstTransactionDatesAsync([asset.Id], cancellationToken);
        await PublishAsync(asset, portfolio.IsArchived, firstDates.GetValueOrDefault(asset.Id), cancellationToken);
    }

    public async Task PublishChangedAsync(Asset asset, CancellationToken cancellationToken)
    {
        var portfolioIsArchived = await dbContext.Portfolios
            .AsNoTracking()
            .Where(p => p.Id == asset.PortfolioId)
            .Select(p => p.IsArchived)
            .FirstAsync(cancellationToken);

        var firstDates = await FirstTransactionDatesAsync([asset.Id], cancellationToken);

        asset.Version++;
        await PublishAsync(asset, portfolioIsArchived, firstDates.GetValueOrDefault(asset.Id), cancellationToken);
    }

    public async Task PublishForEveryAssetAsync(PortfolioEntity portfolio, CancellationToken cancellationToken)
    {
        var assets = await dbContext.Assets
            .Where(a => a.PortfolioId == portfolio.Id)
            .ToListAsync(cancellationToken);

        var firstDates = await FirstTransactionDatesAsync([.. assets.Select(a => a.Id)], cancellationToken);

        foreach (var asset in assets)
        {
            asset.Version++;
            await PublishAsync(asset, portfolio.IsArchived, firstDates.GetValueOrDefault(asset.Id), cancellationToken);
        }
    }

    // Tracked rows win over stored ones: the event is published before the save that writes them.
    private async Task<Dictionary<Guid, DateOnly?>> FirstTransactionDatesAsync(
        IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken)
    {
        var tracked = dbContext.ChangeTracker.Entries<Transaction>()
            .Where(e => assetIds.Contains(e.Entity.AssetId))
            .ToList();
        var trackedIds = tracked.Select(e => e.Entity.Id).ToList();

        var firstDates = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => assetIds.Contains(t.AssetId) && !trackedIds.Contains(t.Id))
            .GroupBy(t => t.AssetId)
            .Select(g => new { AssetId = g.Key, First = g.Min(t => t.Date) })
            .ToDictionaryAsync(x => x.AssetId, x => (DateOnly?)x.First, cancellationToken);

        foreach (var entry in tracked.Where(e => e.State != EntityState.Deleted))
        {
            var date = entry.Entity.Date;
            if (!firstDates.TryGetValue(entry.Entity.AssetId, out var first) || first is null || date < first)
            {
                firstDates[entry.Entity.AssetId] = date;
            }
        }

        return firstDates;
    }

    private async Task PublishAsync(
        Asset asset, bool portfolioIsArchived, DateOnly? firstTransactionDate, CancellationToken cancellationToken)
        // The interceptor stamps Asset.UserId only during SaveChangesAsync, which has not run yet.
        => await publishEndpoint.Publish(new AssetPositionChanged
        {
            AssetId = asset.Id,
            PortfolioId = asset.PortfolioId,
            UserId = dbContext.CurrentUserId,
            AssetClass = asset.AssetClass,
            ValuationMode = asset.ValuationMode,
            InstrumentId = asset.InstrumentId,
            Currency = asset.Currency,
            Quantity = asset.Quantity,
            QuoteUnitsPerQuantity = await QuoteUnitsPerQuantityAsync(asset, cancellationToken),
            ManualValueAmount = asset.ManualValueAmount,
            ManualValueDate = asset.ManualValueDate,
            FirstTransactionDate = firstTransactionDate,
            PortfolioIsArchived = portfolioIsArchived,
            // The asset's own flag; the portfolio fan-out never changes it.
            IsArchived = asset.IsArchived,
            Version = asset.Version,
            OccurredAtUtc = timeProvider.GetUtcNow()
        }, cancellationToken);

    private async Task<decimal> QuoteUnitsPerQuantityAsync(Asset asset, CancellationToken cancellationToken)
    {
        if (asset.AssetClass != AssetClass.PreciousMetal)
        {
            return 1m;
        }

        // Local first: AddMetal and UpdateMetal publish before the holding they staged is saved.
        var tracked = dbContext.MetalHoldings.Local.FirstOrDefault(h => h.AssetId == asset.Id);
        if (tracked is not null)
        {
            return tracked.FineWeightGramsPerPiece;
        }

        return await dbContext.MetalHoldings
            .AsNoTracking()
            .Where(h => h.AssetId == asset.Id)
            .Select(h => h.FineWeightGramsPerPiece)
            .FirstAsync(cancellationToken);
    }
}
