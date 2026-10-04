using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Transfers;

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

        // portfolio_db has no FKs, so the delete cascades to the assets and their transactions explicitly.
        var assets = await dbContext.Assets
            .Where(a => a.PortfolioId == id)
            .ToListAsync(cancellationToken);

        var assetIds = assets.Select(a => a.Id).ToList();
        var transactions = await dbContext.Transactions
            .Where(t => assetIds.Contains(t.AssetId))
            .ToListAsync(cancellationToken);

        dbContext.Transactions.RemoveRange(transactions);

        // Detach, never reverse: a leg outside this portfolio stays an ordinary transaction, so nothing is published for it.
        await dbContext.DetachCounterpartsAsync(transactions, cancellationToken);

        // Interest settlements carry no FK, so they go here, with their assets.
        dbContext.SavingsInterestSettlements.RemoveRange(
            await dbContext.SavingsInterestSettlements.Where(s => assetIds.Contains(s.AssetId)).ToListAsync(cancellationToken));
        dbContext.BondInterestSettlements.RemoveRange(
            await dbContext.BondInterestSettlements.Where(s => assetIds.Contains(s.AssetId)).ToListAsync(cancellationToken));

        dbContext.Assets.RemoveRange(assets);
        dbContext.Portfolios.Remove(portfolio);

        var occurredAtUtc = timeProvider.GetUtcNow();

        // One AssetRemoved per asset: MarketData has no portfolio mapping, and the flag leaves the snapshot to the PortfolioDeleted sweep.
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

        // Every removal and every outbox row commit in this one save.
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent transaction moved an asset's xmin: nothing is deleted, and the user retries.
            return TransactionErrors.ConcurrentModification();
        }

        return Result.Success();
    }
}
