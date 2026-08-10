using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetLatestPricesBatch;

/// <summary>
/// Backs Reporting's valuation consumer (T2.11) — same "latest row per instrument via the
/// (InstrumentId, Date) unique index" shape as SearchInstruments, but bounded by an explicit
/// AsOfDate instead of always taking the newest quote ever seen.
/// </summary>
public sealed class GetLatestPricesBatchHandler(MarketDataDbContext dbContext)
{
    public async Task<LatestPricesBatchResponse> HandleAsync(LatestPricesBatchRequest request, CancellationToken cancellationToken)
    {
        var instrumentIds = request.InstrumentIds.Distinct().ToList();

        var quoteCurrencies = await dbContext.Instruments
            .AsNoTracking()
            .Where(i => instrumentIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.QuoteCurrency, cancellationToken);

        var latestQuotes = await dbContext.PriceQuotes
            .AsNoTracking()
            .Where(q => instrumentIds.Contains(q.InstrumentId) && q.Date <= request.AsOfDate)
            .GroupBy(q => q.InstrumentId)
            .Select(g => g.OrderByDescending(q => q.Date).First())
            .ToListAsync(cancellationToken);

        var quotes = latestQuotes
            .Where(q => quoteCurrencies.ContainsKey(q.InstrumentId))
            .Select(q => new InstrumentQuoteResult
            {
                InstrumentId = q.InstrumentId,
                QuoteCurrency = quoteCurrencies[q.InstrumentId],
                Date = q.Date,
                Close = q.Close,
            })
            .ToList();

        return new LatestPricesBatchResponse { Quotes = quotes };
    }
}
