using System.Globalization;
using System.Net;
using System.Net.Http.Json;

namespace Skarbiec.Portfolio.MarketData;

/// <summary>
/// Calls MarketData's service-only <c>GET /internal/fx/{currency}/rate?date=</c> (ADR-026) with no
/// token (ADR-027). Resilience and service discovery come from <c>ConfigureHttpClientDefaults</c> in
/// ServiceDefaults, and failures are handled exactly like <see cref="MarketDataInstrumentLookupClient"/>.
/// </summary>
public sealed class MarketDataFxRateLookupClient(HttpClient httpClient) : IFxRateLookupClient
{
    public async Task<FxRateLookupResult> GetRateAsync(string currency, DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            var uri = $"/internal/fx/{Uri.EscapeDataString(currency)}/rate?date={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
            using var response = await httpClient.GetAsync(uri, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return FxRateLookupResult.NotFound;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return FxRateLookupResult.Unavailable;
            }

            var body = await response.Content.ReadFromJsonAsync<FxRateResponse>(cancellationToken);

            return body is null ? FxRateLookupResult.Unavailable : FxRateLookupResult.Found(body.Rate);
        }
        catch (OperationCanceledException)
        {
            // Checked first and unconditionally, as in MarketDataInstrumentLookupClient: only the
            // resilience pipeline's own timeout maps to Unavailable; caller cancellation propagates.
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return FxRateLookupResult.Unavailable;
        }
        catch (Exception)
        {
            // Any other failure talking to MarketData (connection refused, DNS, TLS, an open circuit
            // breaker, an unreadable body, ...) fails closed: the caller answers 503.
            return FxRateLookupResult.Unavailable;
        }
    }

    private sealed record FxRateResponse(string Currency, DateOnly Date, decimal Rate);
}
