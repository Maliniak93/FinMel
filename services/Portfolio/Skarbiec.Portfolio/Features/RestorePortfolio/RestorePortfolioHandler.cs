using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.RestorePortfolio;

public sealed class RestorePortfolioHandler(
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

        // Not archived: 200 with the unchanged body and no event.
        if (!portfolio.IsArchived)
        {
            return portfolio.ToResponse(assetCount);
        }

        portfolio.IsArchived = false;

        await publishEndpoint.Publish(new PortfolioRestored
        {
            PortfolioId = portfolio.Id,
            UserId = dbContext.CurrentUserId,
            OccurredAtUtc = timeProvider.GetUtcNow()
        }, cancellationToken);

        // One position event per asset with the cleared flag, so a read model resumes valuing them.
        await positionEventPublisher.PublishForEveryAssetAsync(portfolio, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return portfolio.ToResponse(assetCount);
    }
}
