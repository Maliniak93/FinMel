using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetPricesHistoryBatch;

public sealed class GetPricesHistoryBatchHandler(MarketDataDbContext dbContext)
{
    public async Task<PricesHistoryBatchResponse> HandleAsync(PricesHistoryBatchRequest request, CancellationToken cancellationToken)
    {
        var instrumentIds = request.InstrumentIds.Distinct().ToList();

        var quoteCurrencies = await dbContext.Instruments
            .AsNoTracking()
            .Where(i => instrumentIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.QuoteCurrency, cancellationToken);

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
            .Where(q => quoteCurrencies.ContainsKey(q.InstrumentId))
            .GroupBy(q => q.InstrumentId)
            .Select(g => new InstrumentQuoteSeries
            {
                InstrumentId = g.Key,
                QuoteCurrency = quoteCurrencies[g.Key],
                Quotes = g.OrderBy(q => q.Date).Select(q => new HistoryQuote { Date = q.Date, Close = q.Close }).ToList(),
            })
            .ToList();

        return new PricesHistoryBatchResponse { Series = series };
    }
}
