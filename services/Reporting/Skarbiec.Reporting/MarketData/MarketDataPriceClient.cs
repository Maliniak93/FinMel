using System.Net.Http.Json;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.MarketData;

// A total failure to reach MarketData propagates on purpose: DailyPricesSyncedConsumer relies on the retry.
public sealed class MarketDataPriceClient(HttpClient httpClient) : IPriceQuoteClient
{
    // Comfortably under MarketData's own MaxLength(1000) batch-size cap.
    private const int BatchSize = 500;

    public async Task<IReadOnlyDictionary<Guid, InstrumentPriceLookup>> GetLatestPricesAsync(
        IReadOnlyList<Guid> instrumentIds, DateOnly asOfDate, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, InstrumentPriceLookup>();

        foreach (var batch in instrumentIds.Distinct().Chunk(BatchSize))
        {
            using var response = await httpClient.PostAsJsonAsync(
                "/internal/prices/latest-batch",
                new LatestPricesBatchRequest(batch, asOfDate),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<LatestPricesBatchResponse>(cancellationToken)
                ?? throw new InvalidOperationException("MarketData returned an empty prices batch response.");

            foreach (var quote in body.Quotes)
            {
                result[quote.InstrumentId] = new InstrumentPriceLookup(quote.QuoteCurrency, quote.Date, quote.Close);
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<string, FxRateLookup>> GetLatestFxRatesAsync(
        IReadOnlyList<string> pairs, DateOnly asOfDate, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, FxRateLookup>(StringComparer.OrdinalIgnoreCase);

        foreach (var batch in pairs.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(BatchSize))
        {
            using var response = await httpClient.PostAsJsonAsync(
                "/internal/fx/latest-batch",
                new FxRatesBatchRequest(batch, asOfDate),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<FxRatesBatchResponse>(cancellationToken)
                ?? throw new InvalidOperationException("MarketData returned an empty FX rates batch response.");

            foreach (var rate in body.Rates)
            {
                result[rate.Pair] = new FxRateLookup(rate.Date, rate.Rate);
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<InstrumentPriceLookup>>> GetPriceHistoryAsync(
        IReadOnlyList<Guid> instrumentIds, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, IReadOnlyList<InstrumentPriceLookup>>();

        foreach (var batch in instrumentIds.Distinct().Chunk(BatchSize))
        {
            using var response = await httpClient.PostAsJsonAsync(
                "/internal/prices/history-batch",
                new PricesHistoryBatchRequest(batch, from, to),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<PricesHistoryBatchResponse>(cancellationToken)
                ?? throw new InvalidOperationException("MarketData returned an empty prices history response.");

            foreach (var series in body.Series)
            {
                result[series.InstrumentId] =
                    [.. series.Quotes.Select(q => new InstrumentPriceLookup(series.QuoteCurrency, q.Date, q.Close))];
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<FxRateLookup>>> GetFxHistoryAsync(
        IReadOnlyList<string> pairs, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyList<FxRateLookup>>(StringComparer.OrdinalIgnoreCase);

        foreach (var batch in pairs.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(BatchSize))
        {
            using var response = await httpClient.PostAsJsonAsync(
                "/internal/fx/history-batch",
                new FxHistoryBatchRequest(batch, from, to),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<FxHistoryBatchResponse>(cancellationToken)
                ?? throw new InvalidOperationException("MarketData returned an empty FX history response.");

            foreach (var series in body.Series)
            {
                result[series.Pair] = [.. series.Rates.Select(r => new FxRateLookup(r.Date, r.Rate))];
            }
        }

        return result;
    }

    private sealed record LatestPricesBatchRequest(IReadOnlyList<Guid> InstrumentIds, DateOnly AsOfDate);

    private sealed record LatestPricesBatchResponse
    {
        public required IReadOnlyList<InstrumentQuoteResult> Quotes { get; init; }
    }

    private sealed record InstrumentQuoteResult(Guid InstrumentId, string QuoteCurrency, DateOnly Date, decimal Close);

    private sealed record FxRatesBatchRequest(IReadOnlyList<string> Pairs, DateOnly AsOfDate);

    private sealed record FxRatesBatchResponse
    {
        public required IReadOnlyList<FxRateResult> Rates { get; init; }
    }

    private sealed record FxRateResult(string Pair, DateOnly Date, decimal Rate);

    private sealed record PricesHistoryBatchRequest(IReadOnlyList<Guid> InstrumentIds, DateOnly From, DateOnly To);

    private sealed record PricesHistoryBatchResponse
    {
        public required IReadOnlyList<InstrumentQuoteSeriesResult> Series { get; init; }
    }

    private sealed record InstrumentQuoteSeriesResult(Guid InstrumentId, string QuoteCurrency, IReadOnlyList<HistoryQuoteResult> Quotes);

    private sealed record HistoryQuoteResult(DateOnly Date, decimal Close);

    private sealed record FxHistoryBatchRequest(IReadOnlyList<string> Pairs, DateOnly From, DateOnly To);

    private sealed record FxHistoryBatchResponse
    {
        public required IReadOnlyList<FxRateSeriesResult> Series { get; init; }
    }

    private sealed record FxRateSeriesResult(string Pair, IReadOnlyList<HistoryRateResult> Rates);

    private sealed record HistoryRateResult(DateOnly Date, decimal Rate);
}
