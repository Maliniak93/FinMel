using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.RemoveAsset;

public sealed class RemoveAssetHandler(
    PortfolioDbContext dbContext, IPublishEndpoint publishEndpoint, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await dbContext.Assets
            .FirstOrDefaultAsync(a => a.Id == assetId && a.PortfolioId == portfolioId, cancellationToken);

        if (asset is null)
        {
            return AssetErrors.NotFound(assetId);
        }

        // The transactions themselves answer the guard (spec-02) — no denormalized counter to keep
        // in sync, and the tenancy query filter scopes the check to this user's rows anyway.
        var hasTransactions = await dbContext.Transactions.AnyAsync(t => t.AssetId == assetId, cancellationToken);
        if (hasTransactions)
        {
            return AssetErrors.HasTransactions(assetId);
        }

        dbContext.Assets.Remove(asset);

        // Terminal for this asset (spec-02 design decision 3): no version, no further position
        // event. Published before SaveChangesAsync so it commits with the deletion (ADR-012).
        await publishEndpoint.Publish(new AssetRemoved
        {
            AssetId = asset.Id,
            PortfolioId = portfolioId,
            UserId = dbContext.CurrentUserId,
            OccurredAtUtc = timeProvider.GetUtcNow()
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
