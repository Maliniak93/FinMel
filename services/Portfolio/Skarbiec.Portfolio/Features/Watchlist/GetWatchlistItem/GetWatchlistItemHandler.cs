using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Watchlist.GetWatchlistItem;

public sealed class GetWatchlistItemHandler(
    PortfolioDbContext dbContext, IInstrumentQuoteLookupClient quoteLookup, TimeProvider timeProvider)
{
    public async Task<Result<WatchlistItemResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.WatchlistItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (item is null)
        {
            return WatchlistErrors.NotFound(id);
        }

        return (await new[] { item }.ToResponsesAsync(dbContext, quoteLookup, timeProvider, cancellationToken))[0];
    }
}
