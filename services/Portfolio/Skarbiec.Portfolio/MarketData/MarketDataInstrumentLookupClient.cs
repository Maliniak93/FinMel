using System.Net;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.MarketData;

public sealed class MarketDataInstrumentLookupClient(HttpClient httpClient) : IInstrumentLookupClient
{
    public async Task<InstrumentLookupResult> CheckAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"/internal/instruments/{instrumentId}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return InstrumentLookupResult.NotFound;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return InstrumentLookupResult.Unavailable;
            }

            var body = await response.Content.ReadFromJsonAsync<InstrumentBody>(cancellationToken);
            return body is { AssetClass: { } assetClass, QuoteCurrency: { Length: > 0 } quoteCurrency }
                ? InstrumentLookupResult.Found(assetClass, quoteCurrency)
                : InstrumentLookupResult.Unavailable;
        }
        catch (OperationCanceledException)
        {
            // Checked first and unconditionally: only the resilience pipeline's own timeout maps to Unavailable; caller cancellation propagates.
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return InstrumentLookupResult.Unavailable;
        }
        catch (Exception)
        {
            // Any other transport or body failure fails closed, so the caller answers 503 instead of leaking a 500.
            return InstrumentLookupResult.Unavailable;
        }
    }

    private sealed record InstrumentBody(AssetClass? AssetClass, string? QuoteCurrency);
}
