using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

public static class QuoteUpsert
{
    // Returns the instrument ids that received a quote, so callers can tell which were synced.
    public static async Task<HashSet<Guid>> UpsertInstrumentQuotesAsync(
        MarketDataDbContext db, IReadOnlyList<InstrumentQuote> quotes, CancellationToken cancellationToken)
    {
        if (quotes.Count == 0)
        {
            return [];
        }

        var instrumentIds = quotes.Select(q => q.InstrumentId).ToHashSet();
        var dates = quotes.Select(q => q.Date).ToHashSet();
        var existing = await db.PriceQuotes
            .Where(q => instrumentIds.Contains(q.InstrumentId) && dates.Contains(q.Date))
            .ToDictionaryAsync(q => (q.InstrumentId, q.Date), cancellationToken);

        foreach (var quote in quotes)
        {
            if (existing.TryGetValue((quote.InstrumentId, quote.Date), out var row))
            {
                row.Close = quote.Close;
            }
            else
            {
                db.PriceQuotes.Add(new PriceQuote
                {
                    Id = Guid.NewGuid(),
                    InstrumentId = quote.InstrumentId,
                    Date = quote.Date,
                    Close = quote.Close,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return instrumentIds;
    }

    public static async Task<HashSet<string>> UpsertFxRatesAsync(
        MarketDataDbContext db, IReadOnlyList<FxRateQuote> rates, CancellationToken cancellationToken)
    {
        if (rates.Count == 0)
        {
            return [];
        }

        var pairs = rates.Select(r => r.Pair).ToHashSet();
        var dates = rates.Select(r => r.Date).ToHashSet();
        var existing = await db.FxRates
            .Where(r => pairs.Contains(r.Pair) && dates.Contains(r.Date))
            .ToDictionaryAsync(r => (r.Pair, r.Date), cancellationToken);

        foreach (var rate in rates)
        {
            if (existing.TryGetValue((rate.Pair, rate.Date), out var row))
            {
                row.Rate = rate.Rate;
            }
            else
            {
                db.FxRates.Add(new FxRate
                {
                    Id = Guid.NewGuid(),
                    Pair = rate.Pair,
                    Date = rate.Date,
                    Rate = rate.Rate,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return pairs;
    }
}
