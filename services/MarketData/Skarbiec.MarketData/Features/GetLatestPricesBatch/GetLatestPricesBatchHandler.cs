using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetLatestPricesBatch;

public sealed class GetLatestPricesBatchHandler(MarketDataDbContext dbContext)
{
    public async Task<LatestPricesBatchResponse> HandleAsync(LatestPricesBatchRequest request, CancellationToken cancellationToken)
    {
        var instrumentIds = request.InstrumentIds.Distinct().ToList();

        var instruments = await dbContext.Instruments
            .AsNoTracking()
            .Where(i => instrumentIds.Contains(i.Id))
            .Select(i => new { i.Id, i.QuoteCurrency, i.AssetClass })
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        var latestQuotes = await dbContext.PriceQuotes
            .AsNoTracking()
            .Where(q => instrumentIds.Contains(q.InstrumentId) && q.Date <= request.AsOfDate)
            .GroupBy(q => q.InstrumentId)
            .Select(g => g.OrderByDescending(q => q.Date).First())
            .ToListAsync(cancellationToken);

        var quotes = latestQuotes
            .Where(q => instruments.ContainsKey(q.InstrumentId))
            .Select(q => new InstrumentQuoteResult
            {
                InstrumentId = q.InstrumentId,
                QuoteCurrency = instruments[q.InstrumentId].QuoteCurrency,
                Date = q.Date,
                Close = MetalQuoteUnit.ToServedPrice(instruments[q.InstrumentId].AssetClass, q.Close),
            })
            .ToList();

        return new LatestPricesBatchResponse { Quotes = quotes };
    }
}
