using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetFxRatesHistoryBatch;

public sealed class GetFxRatesHistoryBatchHandler(MarketDataDbContext dbContext)
{
    public async Task<FxRatesHistoryBatchResponse> HandleAsync(FxRatesHistoryBatchRequest request, CancellationToken cancellationToken)
    {
        var pairs = request.Pairs.Distinct().ToList();

        var inRange = await dbContext.FxRates
            .AsNoTracking()
            .Where(r => pairs.Contains(r.Pair) && r.Date >= request.From && r.Date <= request.To)
            .ToListAsync(cancellationToken);

        var lastBefore = await dbContext.FxRates
            .AsNoTracking()
            .Where(r => pairs.Contains(r.Pair) && r.Date < request.From)
            .GroupBy(r => r.Pair)
            .Select(g => g.OrderByDescending(r => r.Date).First())
            .ToListAsync(cancellationToken);

        var series = inRange.Concat(lastBefore)
            .GroupBy(r => r.Pair)
            .Select(g => new FxRateSeries
            {
                Pair = g.Key,
                Rates = g.OrderBy(r => r.Date).Select(r => new HistoryRate { Date = r.Date, Rate = r.Rate }).ToList(),
            })
            .ToList();

        return new FxRatesHistoryBatchResponse { Series = series };
    }
}
