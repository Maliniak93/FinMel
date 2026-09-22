using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features;

/// <summary>
/// The single place that produces <see cref="AssetPositionChanged"/> (spec-02, ADR-021): it owns the
/// <see cref="Asset.Version"/> counter, resolves the owning portfolio's archived flag and stamps
/// <c>OccurredAtUtc</c>. Five mutating slices call it — extraction is the rule here, not a shortcut
/// past ADR-002's "no service layer": this is one event's construction, not a business layer.
/// </summary>
/// <remarks>
/// Every method must be awaited <b>before</b> the caller's <c>SaveChangesAsync</c>, so the outbox row
/// and the business write commit in one transaction (ADR-012). Nothing here calls
/// <c>SaveChangesAsync</c> itself — the version bump is staged on the tracked entity and saved by the
/// caller.
/// </remarks>
public sealed class PositionEventPublisher(
    PortfolioDbContext dbContext, IPublishEndpoint publishEndpoint, TimeProvider timeProvider)
{
    /// <summary>
    /// A brand-new asset, published at version 0 — its first state. The caller already holds the
    /// portfolio (it just validated it), so no lookup is needed.
    /// </summary>
    public Task PublishCreatedAsync(Asset asset, PortfolioEntity portfolio, CancellationToken cancellationToken)
        => PublishAsync(asset, portfolio.IsArchived, cancellationToken);

    /// <summary>
    /// An existing asset whose position moved (edited, or a transaction recorded/edited/deleted
    /// against it). Bumps <see cref="Asset.Version"/> first, so successive events for one asset carry
    /// strictly increasing versions (spec-02 AC-7).
    /// </summary>
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

    /// <summary>
    /// The archive/restore fan-out: one event per asset of <paramref name="portfolio"/>, carrying its
    /// <b>new</b> <see cref="PortfolioEntity.IsArchived"/> value — otherwise a read model would keep
    /// valuing an archived portfolio. The asset rows themselves don't change, which is precisely why
    /// <c>Version</c> is an app-managed counter and not <c>xmin</c> (spec-02 design decision 1).
    /// </summary>
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
        // UserId comes from the DbContext's current user (ADR-006), not from the entity: the
        // interceptor only stamps Asset.UserId during SaveChangesAsync, which hasn't run yet.
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
            Version = asset.Version,
            OccurredAtUtc = timeProvider.GetUtcNow()
        }, cancellationToken);
}
