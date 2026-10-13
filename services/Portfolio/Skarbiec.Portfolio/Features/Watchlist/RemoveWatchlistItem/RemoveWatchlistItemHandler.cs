using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Watchlist.RemoveWatchlistItem;

public sealed class RemoveWatchlistItemHandler(
    PortfolioDbContext dbContext, IPublishEndpoint publishEndpoint, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.WatchlistItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return WatchlistErrors.NotFound(id);
        }

        dbContext.WatchlistItems.Remove(item);

        await publishEndpoint.Publish(new WatchlistItemRemoved
        {
            WatchlistItemId = item.Id,
            UserId = dbContext.CurrentUserId,
            InstrumentId = item.InstrumentId,
            OccurredAtUtc = timeProvider.GetUtcNow()
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
