using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Watchlist.UpdateWatchlistItem;

public sealed class UpdateWatchlistItemHandler(
    PortfolioDbContext dbContext, IInstrumentQuoteLookupClient quoteLookup, TimeProvider timeProvider)
{
    public async Task<Result<WatchlistItemResponse>> HandleAsync(
        Guid id, UpdateWatchlistItemRequest request, CancellationToken cancellationToken)
    {
        var item = await dbContext.WatchlistItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return WatchlistErrors.NotFound(id);
        }

        item.Note = WatchlistReader.NormalizeNote(request.Note);
        item.TargetPrice = request.TargetPrice;

        await dbContext.SaveChangesAsync(cancellationToken);

        return (await new[] { item }.ToResponsesAsync(dbContext, quoteLookup, timeProvider, cancellationToken))[0];
    }
}
