using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Watchlist.ListWatchlist;

public sealed class ListWatchlistHandler(
    PortfolioDbContext dbContext, IInstrumentQuoteLookupClient quoteLookup, TimeProvider timeProvider)
{
    public async Task<Result<IReadOnlyList<WatchlistItemResponse>>> HandleAsync(
        AssetClass? assetClass, CancellationToken cancellationToken)
    {
        if (assetClass is not null and not (AssetClass.Stock or AssetClass.Etf))
        {
            return WatchlistErrors.UnsupportedAssetClass;
        }

        var items = await dbContext.WatchlistItems
            .AsNoTracking()
            .Where(i => assetClass == null || i.AssetClass == assetClass)
            .ToListAsync(cancellationToken);

        var ordered = items
            .OrderBy(i => i.Ticker, StringComparer.Ordinal)
            .ThenBy(i => i.Name, StringComparer.Ordinal)
            .ToList();

        return Result<IReadOnlyList<WatchlistItemResponse>>.Success(
            await ordered.ToResponsesAsync(dbContext, quoteLookup, timeProvider, cancellationToken));
    }
}
