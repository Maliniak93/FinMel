using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.ArchivePortfolio;

public sealed class ArchivePortfolioHandler(
    PortfolioDbContext dbContext,
    PositionEventPublisher positionEventPublisher,
    IPublishEndpoint publishEndpoint,
    TimeProvider timeProvider)
{
    public async Task<Result<PortfolioResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var portfolio = await dbContext.Portfolios.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (portfolio is null)
        {
            return PortfolioErrors.NotFound(id);
        }

        var assetCount = await dbContext.Assets.CountAsync(a => a.PortfolioId == id, cancellationToken);

        // Already archived: 200 with no event, so a repeated click fans out no position events.
        if (portfolio.IsArchived)
        {
            return portfolio.ToResponse(assetCount);
        }

        portfolio.IsArchived = true;

        await publishEndpoint.Publish(new PortfolioArchived
        {
            PortfolioId = portfolio.Id,
            UserId = dbContext.CurrentUserId,
            OccurredAtUtc = timeProvider.GetUtcNow()
        }, cancellationToken);

        // One position event per asset with the new flag, or a read model would keep valuing an archived portfolio.
        await positionEventPublisher.PublishForEveryAssetAsync(portfolio, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return portfolio.ToResponse(assetCount);
    }
}
