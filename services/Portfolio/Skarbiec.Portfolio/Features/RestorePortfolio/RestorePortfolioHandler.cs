using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.RestorePortfolio;

/// <summary>The mirror of <c>ArchivePortfolioHandler</c> — same shape, opposite flag (spec-02).</summary>
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

        // Not archived: 200 with the unchanged body and no event (spec-02 AC-10, design decision 2).
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

        // Plus one position event per asset carrying the cleared archived flag, in this same
        // transaction — the counterpart of the archive fan-out, so a read model resumes valuing them.
        await positionEventPublisher.PublishForEveryAssetAsync(portfolio, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return portfolio.ToResponse(assetCount);
    }
}
