using System.Net.Http.Json;
using Skarbiec.Reporting.Valuation;

namespace Skarbiec.Reporting.MarketData;

/// <summary>
/// Calls MarketData's <c>POST /api/marketdata/prices/latest-batch</c> and <c>/api/marketdata/fx/latest-batch</c>
/// (T2.11). Resilience and service discovery come from ServiceDefaults; a total failure to reach
/// MarketData is deliberately left to propagate — see <c>DailyPricesSyncedConsumer</c> for why.
/// </summary>
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
                "/api/marketdata/prices/latest-batch",
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
                "/api/marketdata/fx/latest-batch",
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
}
