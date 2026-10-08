using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetInstrumentsBatch;

public sealed class GetInstrumentsBatchHandler(MarketDataDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<InstrumentsBatchResponse> HandleAsync(InstrumentsBatchRequest request, CancellationToken cancellationToken)
    {
        var instrumentIds = request.InstrumentIds.Distinct().ToList();
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var instruments = await dbContext.Instruments
            .AsNoTracking()
            .Where(i => instrumentIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        var latestQuotes = await dbContext.PriceQuotes
            .AsNoTracking()
            .Where(q => instrumentIds.Contains(q.InstrumentId) && q.Date <= today)
            .GroupBy(q => q.InstrumentId)
            .Select(g => g.OrderByDescending(q => q.Date).First())
            .ToDictionaryAsync(q => q.InstrumentId, cancellationToken);

        var items = instruments
            .Select(i =>
            {
                latestQuotes.TryGetValue(i.Id, out var quote);

                return new InstrumentBatchItem
                {
                    InstrumentId = i.Id,
                    Ticker = i.Ticker,
                    Name = i.Name,
                    Exchange = i.Exchange,
                    QuoteCurrency = i.QuoteCurrency,
                    LastPrice = quote?.Close,
                    LastPriceDate = quote?.Date
                };
            })
            .ToList();

        return new InstrumentsBatchResponse { Instruments = items };
    }
}
