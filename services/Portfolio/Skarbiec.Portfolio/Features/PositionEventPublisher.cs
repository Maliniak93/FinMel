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
        var transactions = await LoadTransactionsAsync([asset.Id], cancellationToken);
        await PublishAsync(asset, portfolio.IsArchived, HistoryOf(transactions, asset.Id), cancellationToken);
    }

    public async Task PublishChangedAsync(Asset asset, CancellationToken cancellationToken)
    {
        var portfolioIsArchived = await dbContext.Portfolios
            .AsNoTracking()
            .Where(p => p.Id == asset.PortfolioId)
            .Select(p => p.IsArchived)
            .FirstAsync(cancellationToken);

        var transactions = await LoadTransactionsAsync([asset.Id], cancellationToken);

        asset.Version++;
        await PublishAsync(asset, portfolioIsArchived, HistoryOf(transactions, asset.Id), cancellationToken);
    }

    public async Task PublishForEveryAssetAsync(PortfolioEntity portfolio, CancellationToken cancellationToken)
    {
        var assets = await dbContext.Assets
            .Where(a => a.PortfolioId == portfolio.Id)
            .ToListAsync(cancellationToken);

        var transactions = await LoadTransactionsAsync([.. assets.Select(a => a.Id)], cancellationToken);

        foreach (var asset in assets)
        {
            asset.Version++;
            await PublishAsync(asset, portfolio.IsArchived, HistoryOf(transactions, asset.Id), cancellationToken);
        }
    }

    // Tracked rows win over stored ones: the event is published before the save that writes them.
    private async Task<Dictionary<Guid, List<Transaction>>> LoadTransactionsAsync(
        IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken)
    {
        var tracked = dbContext.ChangeTracker.Entries<Transaction>()
            .Where(e => assetIds.Contains(e.Entity.AssetId))
            .ToList();
        var trackedIds = tracked.Select(e => e.Entity.Id).ToList();

        var stored = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => assetIds.Contains(t.AssetId) && !trackedIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        return stored
            .Concat(tracked.Where(e => e.State != EntityState.Deleted).Select(e => e.Entity))
            .GroupBy(t => t.AssetId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static IReadOnlyList<QuantityPoint> HistoryOf(Dictionary<Guid, List<Transaction>> transactions, Guid assetId)
    {
        if (!transactions.TryGetValue(assetId, out var list))
        {
            return [];
        }

        var history = TransactionQuantityCalculator.History(list);
        return history.IsSuccess
            ? history.Value
            : throw new InvalidOperationException($"Asset {assetId} has an invalid transaction history: {history.Error.Code}.");
    }

    private async Task PublishAsync(
        Asset asset, bool portfolioIsArchived, IReadOnlyList<QuantityPoint> quantityHistory, CancellationToken cancellationToken)
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
            FirstTransactionDate = quantityHistory.Count == 0 ? null : quantityHistory[0].Date,
            QuantityHistory = quantityHistory,
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
