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

        if (await dbContext.IsPortfolioArchivedAsync(portfolioId, cancellationToken))
        {
            return PortfolioErrors.Archived(portfolioId);
        }

        // The delete cascades to the asset's transactions explicitly — portfolio_db has no FKs, so
        // the database cascades nothing (spec-08). The tenancy query filter scopes the load to this
        // user's rows.
        var transactions = await dbContext.Transactions
            .Where(t => t.AssetId == assetId)
            .ToListAsync(cancellationToken);

        dbContext.Transactions.RemoveRange(transactions);
        dbContext.Assets.Remove(asset);

        // Terminal for this asset (spec-02 design decision 3): no version, no further position
        // event. Published before SaveChangesAsync so it commits with the deletions (ADR-012).
        await publishEndpoint.Publish(new AssetRemoved
        {
            AssetId = asset.Id,
            PortfolioId = portfolioId,
            UserId = dbContext.CurrentUserId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            CascadedFromPortfolio = false
        }, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A transaction recorded concurrently moved the asset's xmin: nothing is deleted, so no
            // orphan transaction is left behind, and the user retries (spec-08).
            return TransactionErrors.ConcurrentModification();
        }

        return Result.Success();
    }
}
