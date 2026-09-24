using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetFxRate;

/// <summary>
/// Backs Portfolio's FX rate lookup (ADR-026): the latest <c>{currency}PLN</c> rate on or before
/// <c>date</c> — the same "last rate on or before" rule as <c>GetFxRatesBatch</c> and Reporting's
/// valuation, for a single currency.
/// </summary>
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
