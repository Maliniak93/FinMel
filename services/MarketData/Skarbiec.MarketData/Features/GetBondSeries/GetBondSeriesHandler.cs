using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetBondSeries;

public sealed class GetBondSeriesHandler(MarketDataDbContext dbContext)
{
    public async Task<Result<BondSeriesResponse>> HandleAsync(string code, CancellationToken cancellationToken)
    {
        var series = await dbContext.BondSeries
            .AsNoTracking()
            .Where(s => s.Code == code)
            .Select(s => new BondSeriesResponse(
                s.Code,
                s.Type,
                s.Isin,
                s.SaleStart,
                s.SaleEnd,
                s.IssuePrice,
                s.SwapPrice,
                s.MarginPercent,
                s.PeriodRates
                    .OrderBy(r => r.PeriodIndex)
                    .Select(r => new BondSeriesPeriodRateResponse(r.PeriodIndex, r.RatePercent))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        if (series is null)
        {
            return BondSeriesErrors.NotFound(code);
        }

        return series;
    }
}
