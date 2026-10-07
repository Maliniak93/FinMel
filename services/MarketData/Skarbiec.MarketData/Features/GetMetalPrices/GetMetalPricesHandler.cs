using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources.GoldApi;

namespace Skarbiec.MarketData.Features.GetMetalPrices;

public sealed class GetMetalPricesHandler(MarketDataDbContext dbContext, TimeProvider timeProvider)
{
    private const string UsdPlnPair = "USDPLN";
    private const int StaleAfterDays = 7;

    // InvariantGlobalization leaves Windows without ICU, so it needs its registry id; Linux resolves the IANA id from tzdata.
    private static readonly TimeZoneInfo Warsaw =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out var iana)
            ? iana
            : TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

    public async Task<Result<List<MetalPriceResponse>>> HandleAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Warsaw).DateTime);
        var items = new List<MetalPriceResponse>();

        foreach (var metal in Enum.GetValues<Metal>())
        {
            items.Add(await PriceOfAsync(metal, today, cancellationToken));
        }

        return items;
    }

    private async Task<MetalPriceResponse> PriceOfAsync(Metal metal, DateOnly today, CancellationToken cancellationToken)
    {
        var instrumentId = MetalInstruments.InstrumentIdFor(metal);

        var quote = await dbContext.PriceQuotes
            .AsNoTracking()
            .Where(q => q.InstrumentId == instrumentId)
            .OrderByDescending(q => q.Date)
            .Select(q => new { q.Date, q.Close })
            .FirstOrDefaultAsync(cancellationToken);

        if (quote is null)
        {
            return new MetalPriceResponse { Metal = metal, InstrumentId = instrumentId, IsStale = false };
        }

        var rate = await dbContext.FxRates
            .AsNoTracking()
            .Where(r => r.Pair == UsdPlnPair && r.Date <= quote.Date)
            .OrderByDescending(r => r.Date)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync(cancellationToken);

        var pricePerGramPln = quote.Close * rate;

        return new MetalPriceResponse
        {
            Metal = metal,
            InstrumentId = instrumentId,
            Date = quote.Date,
            PricePerGramUsd = quote.Close,
            UsdPlnRate = rate,
            PricePerGramPln = RoundToCents(pricePerGramPln),
            PricePerTroyOuncePln = RoundToCents(pricePerGramPln * GoldApiPriceSource.GramsPerTroyOunce),
            IsStale = quote.Date < today.AddDays(-StaleAfterDays),
        };
    }

    private static decimal? RoundToCents(decimal? value) =>
        value is { } amount ? Math.Round(amount, 2, MidpointRounding.AwayFromZero) : null;
}
