using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.DeletePortfolio;

public sealed class DeletePortfolioHandler(
    PortfolioDbContext dbContext, IPublishEndpoint publishEndpoint, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (portfolio is null)
        {
            return PortfolioErrors.NotFound(id);
        }

        // The delete cascades to the assets and their transactions explicitly — portfolio_db has no
        // FKs, so the database cascades nothing (spec-08). Both loads go through the tenancy query
        // filter, so they only ever see this user's rows.
        var assets = await dbContext.Assets
            .Where(a => a.PortfolioId == id)
            .ToListAsync(cancellationToken);

        var assetIds = assets.Select(a => a.Id).ToList();
        var transactions = await dbContext.Transactions
            .Where(t => assetIds.Contains(t.AssetId))
            .ToListAsync(cancellationToken);

        dbContext.Transactions.RemoveRange(transactions);
        dbContext.Assets.RemoveRange(assets);
        dbContext.Portfolios.Remove(portfolio);

        var occurredAtUtc = timeProvider.GetUtcNow();

        // One AssetRemoved per asset: MarketData tracks instrument usage per asset and has no
        // portfolio mapping. The flag tells Reporting to leave the portfolio's snapshot to the
        // PortfolioDeleted sweep instead of revaluing it (spec-08 design decisions).
        foreach (var asset in assets)
        {
            await publishEndpoint.Publish(new AssetRemoved
            {
                AssetId = asset.Id,
                PortfolioId = portfolio.Id,
                UserId = dbContext.CurrentUserId,
                OccurredAtUtc = occurredAtUtc,
                CascadedFromPortfolio = true
            }, cancellationToken);
        }

        await publishEndpoint.Publish(new PortfolioDeleted
        {
            PortfolioId = portfolio.Id,
            UserId = dbContext.CurrentUserId,
            OccurredAtUtc = occurredAtUtc
        }, cancellationToken);

        // Every removal and every outbox row commit in this one save (ADR-012).
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A transaction recorded concurrently moved an asset's xmin: nothing is deleted, so no
            // orphan transaction is left behind, and the user retries (spec-08).
            return TransactionErrors.ConcurrentModification();
        }

        return Result.Success();
    }
}
