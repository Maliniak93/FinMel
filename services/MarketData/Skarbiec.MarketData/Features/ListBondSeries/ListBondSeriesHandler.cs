using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.ListBondSeries;

public sealed class ListBondSeriesHandler(MarketDataDbContext dbContext, TimeProvider timeProvider)
{
    // InvariantGlobalization leaves Windows without ICU, so it needs its registry id; Linux resolves the IANA id from tzdata.
    private static readonly TimeZoneInfo Warsaw =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out var iana)
            ? iana
            : TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

    public async Task<IReadOnlyList<BondSeriesListItem>> HandleAsync(DateOnly? onSaleOn, CancellationToken cancellationToken)
    {
        var date = onSaleOn ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Warsaw).DateTime);

        return await dbContext.BondSeries
            .AsNoTracking()
            .Where(s => s.SaleStart <= date && date <= s.SaleEnd)
            .OrderBy(s => s.Type)
            .ThenBy(s => s.Code)
            .Select(s => new BondSeriesListItem(
                s.Code,
                s.Type,
                s.Isin,
                s.SaleStart,
                s.SaleEnd,
                s.IssuePrice,
                s.SwapPrice,
                s.MarginPercent,
                s.PeriodRates.Where(r => r.PeriodIndex == 0).Select(r => (decimal?)r.RatePercent).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }
}
