using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetPricesHistoryBatch;

public sealed class GetPricesHistoryBatchHandler(MarketDataDbContext dbContext)
{
    public async Task<PricesHistoryBatchResponse> HandleAsync(PricesHistoryBatchRequest request, CancellationToken cancellationToken)
    {
        var instrumentIds = request.InstrumentIds.Distinct().ToList();

        var instruments = await dbContext.Instruments
            .AsNoTracking()
            .Where(i => instrumentIds.Contains(i.Id))
            .Select(i => new { i.Id, i.QuoteCurrency, i.AssetClass })
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        var inRange = await dbContext.PriceQuotes
            .AsNoTracking()
            .Where(q => instrumentIds.Contains(q.InstrumentId) && q.Date >= request.From && q.Date <= request.To)
            .ToListAsync(cancellationToken);

        var lastBefore = await dbContext.PriceQuotes
            .AsNoTracking()
            .Where(q => instrumentIds.Contains(q.InstrumentId) && q.Date < request.From)
            .GroupBy(q => q.InstrumentId)
            .Select(g => g.OrderByDescending(q => q.Date).First())
            .ToListAsync(cancellationToken);

        var series = inRange.Concat(lastBefore)
            .Where(q => instruments.ContainsKey(q.InstrumentId))
            .GroupBy(q => q.InstrumentId)
            .Select(g => new InstrumentQuoteSeries
            {
                InstrumentId = g.Key,
                QuoteCurrency = instruments[g.Key].QuoteCurrency,
                Quotes = g.OrderBy(q => q.Date)
                    .Select(q => new HistoryQuote { Date = q.Date, Close = MetalQuoteUnit.ToServedPrice(instruments[g.Key].AssetClass, q.Close) })
                    .ToList(),
            })
            .ToList();

        return new PricesHistoryBatchResponse { Series = series };
    }
}
