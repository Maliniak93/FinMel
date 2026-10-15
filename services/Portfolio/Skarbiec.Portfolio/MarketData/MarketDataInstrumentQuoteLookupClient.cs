using System.Net;
using System.Net.Http.Json;

namespace Skarbiec.Portfolio.MarketData;

public sealed class MarketDataInstrumentQuoteLookupClient(HttpClient httpClient) : IInstrumentQuoteLookupClient
{
    private const string BaseCurrency = "PLN";

    public async Task<InstrumentQuoteLookupResult> GetQuotesAsync(
        IReadOnlyCollection<Guid> instrumentIds, DateOnly asOfDate, CancellationToken cancellationToken)
    {
        try
        {
            using var instrumentsResponse = await httpClient.PostAsJsonAsync(
                "/internal/instruments/batch", new InstrumentsBatchRequest(instrumentIds), cancellationToken);

            if (instrumentsResponse.StatusCode != HttpStatusCode.OK)
            {
                return InstrumentQuoteLookupResult.Unavailable;
            }

            var instrumentsBody = await instrumentsResponse.Content.ReadFromJsonAsync<InstrumentsBatchResponse>(cancellationToken);
            if (instrumentsBody is null)
            {
                return InstrumentQuoteLookupResult.Unavailable;
            }

            var instruments = instrumentsBody.Instruments.ToDictionary(
                i => i.InstrumentId,
                i => new InstrumentQuote(i.InstrumentId, i.Ticker, i.Name, i.Exchange, i.QuoteCurrency, i.LastPrice, i.LastPriceDate, i.PreviousClose, i.PreviousCloseDate));

            var currencies = instruments.Values
                .Select(i => i.QuoteCurrency)
                .Where(c => c != BaseCurrency)
                .Distinct()
                .ToList();

            var rates = await FetchRatesAsync(currencies, asOfDate, cancellationToken);

            return rates is null
                ? InstrumentQuoteLookupResult.Unavailable
                : InstrumentQuoteLookupResult.Found(instruments, rates);
        }
        catch (OperationCanceledException)
        {
            // Checked first and unconditionally: only the resilience pipeline's own timeout maps to Unavailable; caller cancellation propagates.
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return InstrumentQuoteLookupResult.Unavailable;
        }
        catch (Exception)
        {
            // Any other failure talking to MarketData fails soft: the caller lists the holdings without prices.
            return InstrumentQuoteLookupResult.Unavailable;
        }
    }

    public async Task<InstrumentQuoteLookupResult> GetRatesAsync(
        IReadOnlyCollection<string> currencies, DateOnly asOfDate, CancellationToken cancellationToken)
    {
        try
        {
            var rates = await FetchRatesAsync(currencies, asOfDate, cancellationToken);

            return rates is null
                ? InstrumentQuoteLookupResult.Unavailable
                : InstrumentQuoteLookupResult.Found(new Dictionary<Guid, InstrumentQuote>(), rates);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return InstrumentQuoteLookupResult.Unavailable;
        }
        catch (Exception)
        {
            return InstrumentQuoteLookupResult.Unavailable;
        }
    }

    private async Task<Dictionary<string, decimal>?> FetchRatesAsync(
        IReadOnlyCollection<string> currencies, DateOnly asOfDate, CancellationToken cancellationToken)
    {
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (currencies.Count == 0)
        {
            return rates;
        }

        using var ratesResponse = await httpClient.PostAsJsonAsync(
            "/internal/fx/latest-batch",
            new FxBatchRequest([.. currencies.Select(c => c + BaseCurrency)], asOfDate),
            cancellationToken);

        if (ratesResponse.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        var ratesBody = await ratesResponse.Content.ReadFromJsonAsync<FxBatchResponse>(cancellationToken);
        if (ratesBody is null)
        {
            return null;
        }

        foreach (var rate in ratesBody.Rates)
        {
            rates[rate.Pair[..^BaseCurrency.Length]] = rate.Rate;
        }

        return rates;
    }

    private sealed record InstrumentsBatchRequest(IReadOnlyCollection<Guid> InstrumentIds);

    private sealed record InstrumentItem(
        Guid InstrumentId,
        string Ticker,
        string Name,
        string? Exchange,
        string QuoteCurrency,
        decimal? LastPrice,
        DateOnly? LastPriceDate,
        decimal? PreviousClose,
        DateOnly? PreviousCloseDate);

    private sealed record InstrumentsBatchResponse(IReadOnlyList<InstrumentItem> Instruments);

    private sealed record FxBatchRequest(IReadOnlyList<string> Pairs, DateOnly AsOfDate);

    private sealed record FxRateItem(string Pair, decimal Rate);

    private sealed record FxBatchResponse(IReadOnlyList<FxRateItem> Rates);
}
