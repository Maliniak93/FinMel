using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Features.SearchInstruments;

public sealed class SearchInstrumentsHandler(MarketDataDbContext dbContext, IInstrumentSearchSource searchSource)
{
    private const int MaxLimit = 50;
    private const int MinProviderQueryLength = 2;

    public async Task<InstrumentSearchResponse> HandleAsync(
        string? query, AssetClass? assetClass, int limit, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);

        if (string.IsNullOrWhiteSpace(query))
        {
            return new InstrumentSearchResponse([], ProviderUnavailable: false);
        }

        var trimmed = query.Trim();
        var pattern = trimmed + "%";

        var localQuery = dbContext.Instruments
            .AsNoTracking()
            .Where(i => EF.Functions.ILike(i.Ticker, pattern) || EF.Functions.ILike(i.Name, pattern));
        if (assetClass is { } localClass)
        {
            localQuery = localQuery.Where(i => i.AssetClass == localClass);
        }

        var localRows = await localQuery
            .OrderBy(i => i.Ticker)
            .Take(limit)
            .ToListAsync(cancellationToken);

        if (assetClass is not (AssetClass.Stock or AssetClass.Etf) || trimmed.Length < MinProviderQueryLength)
        {
            return new InstrumentSearchResponse(await ToResultsAsync(localRows, [], [], limit, cancellationToken), ProviderUnavailable: false);
        }

        var outcome = await searchSource.SearchAsync(trimmed, cancellationToken);
        if (outcome.IsUnavailable)
        {
            return new InstrumentSearchResponse(await ToResultsAsync(localRows, [], [], limit, cancellationToken), ProviderUnavailable: true);
        }

        var localTickers = localRows.Select(i => i.Ticker).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A listing of another class would only be rejected by Portfolio's class check once picked.
        var candidates = outcome.Candidates
            .Where(c => c.AssetClass == assetClass && !localTickers.Contains(c.Ticker))
            .DistinctBy(c => c.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A listing already stored but missed by the prefix match is shown as known, never as new.
        var source = AssetClassPriceSourceMapping.Resolve(assetClass.Value);
        var candidateTickers = candidates.Select(c => c.Ticker).ToList();
        var knownRows = await dbContext.Instruments
            .AsNoTracking()
            .Where(i => i.Source == source && i.AssetClass == assetClass && candidateTickers.Contains(i.Ticker))
            .ToListAsync(cancellationToken);

        return new InstrumentSearchResponse(
            await ToResultsAsync(localRows, candidates, knownRows, limit, cancellationToken), ProviderUnavailable: false);
    }

    private async Task<IReadOnlyList<InstrumentSearchResult>> ToResultsAsync(
        List<Instrument> localRows,
        List<InstrumentCandidate> candidates,
        List<Instrument> knownRows,
        int limit,
        CancellationToken cancellationToken)
    {
        var knownByTicker = knownRows.ToDictionary(i => i.Ticker, StringComparer.OrdinalIgnoreCase);
        var ordered = localRows
            .Select(i => (Instrument: (Instrument?)i, Candidate: (InstrumentCandidate?)null))
            .Concat(candidates.Select(c => (Instrument: knownByTicker.GetValueOrDefault(c.Ticker), Candidate: (InstrumentCandidate?)c)))
            .Take(limit)
            .ToList();

        if (ordered.Count == 0)
        {
            return [];
        }

        var instrumentIds = ordered.Where(o => o.Instrument is not null).Select(o => o.Instrument!.Id).ToList();

        // A second cheap round trip instead of a correlated subquery per matched instrument.
        var latestQuotes = await dbContext.PriceQuotes
            .AsNoTracking()
            .Where(q => instrumentIds.Contains(q.InstrumentId))
            .GroupBy(q => q.InstrumentId)
            .Select(g => g.OrderByDescending(q => q.Date).First())
            .ToDictionaryAsync(q => q.InstrumentId, cancellationToken);

        return ordered
            .Select(o =>
            {
                if (o.Instrument is { } i)
                {
                    latestQuotes.TryGetValue(i.Id, out var quote);
                    return new InstrumentSearchResult(
                        i.Id, i.Ticker, i.Name, i.AssetClass, i.QuoteCurrency, i.Exchange, i.VerificationStatus, quote?.Close, quote?.Date);
                }

                var c = o.Candidate!;
                return new InstrumentSearchResult(null, c.Ticker, c.Name, c.AssetClass, c.QuoteCurrency, c.Exchange, null, null, null);
            })
            .ToList();
    }
}
