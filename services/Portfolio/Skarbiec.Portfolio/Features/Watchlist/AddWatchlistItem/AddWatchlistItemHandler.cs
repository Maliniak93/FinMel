using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Watchlist.AddWatchlistItem;

public sealed class AddWatchlistItemHandler(
    PortfolioDbContext dbContext,
    IInstrumentLookupClient instrumentLookupClient,
    IInstrumentQuoteLookupClient quoteLookup,
    IPublishEndpoint publishEndpoint,
    TimeProvider timeProvider)
{
    public async Task<Result<WatchlistItemResponse>> HandleAsync(AddWatchlistItemRequest request, CancellationToken cancellationToken)
    {
        var alreadyWatched = await dbContext.WatchlistItems
            .AsNoTracking()
            .AnyAsync(i => i.InstrumentId == request.InstrumentId, cancellationToken);

        if (alreadyWatched)
        {
            return WatchlistErrors.AlreadyWatched(request.InstrumentId);
        }

        var lookup = await instrumentLookupClient.CheckAsync(request.InstrumentId, cancellationToken);
        if (lookup.Status == InstrumentLookupStatus.NotFound)
        {
            return AssetErrors.InstrumentNotFound(request.InstrumentId);
        }

        if (lookup.Status == InstrumentLookupStatus.Unavailable)
        {
            return AssetErrors.MarketDataUnavailable;
        }

        if (lookup.AssetClass is not (AssetClass.Stock or AssetClass.Etf))
        {
            return WatchlistErrors.UnsupportedAssetClass;
        }

        var item = new WatchlistItem
        {
            Id = Guid.NewGuid(),
            InstrumentId = request.InstrumentId,
            AssetClass = lookup.AssetClass.Value,
            Ticker = lookup.Ticker!,
            Name = lookup.Name!,
            Exchange = lookup.Exchange,
            QuoteCurrency = lookup.QuoteCurrency!,
            Note = WatchlistReader.NormalizeNote(request.Note),
            TargetPrice = request.TargetPrice,
            AddedAtUtc = timeProvider.GetUtcNow()
        };

        dbContext.WatchlistItems.Add(item);

        await publishEndpoint.Publish(new WatchlistItemAdded
        {
            WatchlistItemId = item.Id,
            UserId = dbContext.CurrentUserId,
            InstrumentId = item.InstrumentId,
            OccurredAtUtc = item.AddedAtUtc
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return (await new[] { item }.ToResponsesAsync(dbContext, quoteLookup, timeProvider, cancellationToken))[0];
    }
}
