using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetFxRate;

// The same "last rate on or before" rule as GetFxRatesBatch and Reporting's valuation.
public sealed class GetFxRateHandler(MarketDataDbContext dbContext)
{
    public async Task<Result<FxRateResponse>> HandleAsync(string currency, DateOnly date, CancellationToken cancellationToken)
    {
        var code = currency.Trim().ToUpperInvariant();
        var pair = $"{code}{Money.BaseCurrency}";

        var rate = await dbContext.FxRates
            .AsNoTracking()
            .Where(r => r.Pair == pair && r.Date <= date)
            .OrderByDescending(r => r.Date)
            .Select(r => new FxRateResponse(code, r.Date, r.Rate))
            .FirstOrDefaultAsync(cancellationToken);

        return rate is null ? FxRateErrors.NotFound(code, date) : rate;
    }
}
