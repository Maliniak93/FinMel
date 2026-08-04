using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

/// <summary>
/// Upsert-by-(instrument, date)/(pair, date) shared by every job that writes <see cref="PriceQuote"/>/
/// <see cref="FxRate"/> rows — <see cref="PriceSyncJob"/> (T2.6) and <see cref="HistoryBackfillJob"/>
/// (T2.7) both rely on the same idempotency guarantee, so it lives in exactly one place instead of two
/// copies drifting apart.
/// </summary>
public static class QuoteUpsert
{
    /// <summary>Returns the distinct instrument ids that received a quote — callers use this to tell
    /// which of the requested instruments actually got synced.</summary>
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

    /// <summary>Returns the distinct pairs that received a rate — see <see cref="UpsertInstrumentQuotesAsync"/>.</summary>
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
