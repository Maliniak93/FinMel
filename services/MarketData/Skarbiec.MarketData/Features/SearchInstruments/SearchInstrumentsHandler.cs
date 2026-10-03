using Microsoft.EntityFrameworkCore;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.SearchInstruments;

public sealed class SearchInstrumentsHandler(MarketDataDbContext dbContext)
{
    private const int MaxLimit = 50;

    public async Task<IReadOnlyList<InstrumentSearchResult>> HandleAsync(string? query, int limit, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);

        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var pattern = query.Trim() + "%";

        var instruments = await dbContext.Instruments
            .AsNoTracking()
            .Where(i => EF.Functions.ILike(i.Ticker, pattern) || EF.Functions.ILike(i.Name, pattern))
            .OrderBy(i => i.Ticker)
            .Take(limit)
            .ToListAsync(cancellationToken);

        if (instruments.Count == 0)
        {
            return [];
        }

        var instrumentIds = instruments.Select(i => i.Id).ToList();

        // A second cheap round trip instead of a correlated subquery per matched instrument.
        var latestQuotes = await dbContext.PriceQuotes
            .AsNoTracking()
            .Where(q => instrumentIds.Contains(q.InstrumentId))
            .GroupBy(q => q.InstrumentId)
            .Select(g => g.OrderByDescending(q => q.Date).First())
            .ToDictionaryAsync(q => q.InstrumentId, cancellationToken);

        return instruments
            .Select(i =>
            {
                latestQuotes.TryGetValue(i.Id, out var quote);
                return new InstrumentSearchResult(
                    i.Id,
                    i.Ticker,
                    i.Name,
                    i.AssetClass,
                    i.QuoteCurrency,
                    i.VerificationStatus,
                    quote?.Close,
                    quote?.Date);
            })
            .ToList();
    }
}
