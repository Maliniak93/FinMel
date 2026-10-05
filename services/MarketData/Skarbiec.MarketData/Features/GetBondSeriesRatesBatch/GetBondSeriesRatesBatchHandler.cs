using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetBondSeriesRatesBatch;

public sealed class GetBondSeriesRatesBatchHandler(MarketDataDbContext dbContext)
{
    public async Task<BondSeriesRatesBatchResponse> HandleAsync(BondSeriesRatesBatchRequest request, CancellationToken cancellationToken)
    {
        var codes = request.Codes.Distinct().ToList();

        var series = await dbContext.BondSeries
            .AsNoTracking()
            .Where(s => codes.Contains(s.Code))
            .OrderBy(s => s.Code)
            .Select(s => new BondSeriesRatesResult(
                s.Code,
                s.PeriodRates
                    .OrderBy(r => r.PeriodIndex)
                    .Select(r => new BondSeriesPeriodRateResult(r.PeriodIndex, r.RatePercent))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return new BondSeriesRatesBatchResponse(series);
    }
}
