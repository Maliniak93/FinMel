using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetFxRatesBatch;

public sealed class GetFxRatesBatchHandler(MarketDataDbContext dbContext)
{
    public async Task<FxRatesBatchResponse> HandleAsync(FxRatesBatchRequest request, CancellationToken cancellationToken)
    {
        var pairs = request.Pairs.Distinct().ToList();

        var latestRates = await dbContext.FxRates
            .AsNoTracking()
            .Where(r => pairs.Contains(r.Pair) && r.Date <= request.AsOfDate)
            .GroupBy(r => r.Pair)
            .Select(g => g.OrderByDescending(r => r.Date).First())
            .ToListAsync(cancellationToken);

        var rates = latestRates
            .Select(r => new FxRateResult { Pair = r.Pair, Date = r.Date, Rate = r.Rate })
            .ToList();

        return new FxRatesBatchResponse { Rates = rates };
    }
}
