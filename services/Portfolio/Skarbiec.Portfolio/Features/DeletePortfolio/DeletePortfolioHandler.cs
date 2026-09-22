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

        // The assets themselves answer the guard (spec-02) — no denormalized counter to keep in sync.
        var hasAssets = await dbContext.Assets.AnyAsync(a => a.PortfolioId == id, cancellationToken);
        if (hasAssets)
        {
            return PortfolioErrors.HasAssets(id);
        }

        dbContext.Portfolios.Remove(portfolio);

        // Only an empty portfolio reaches this line, so there is no asset fan-out to publish
        // alongside it. Published before SaveChangesAsync so it commits with the deletion (ADR-012).
        await publishEndpoint.Publish(new PortfolioDeleted
        {
            PortfolioId = portfolio.Id,
            UserId = dbContext.CurrentUserId,
            OccurredAtUtc = timeProvider.GetUtcNow()
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
