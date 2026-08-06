using System.Net;

namespace Skarbiec.Portfolio.MarketData;

/// <summary>
/// Calls MarketData's <c>GET /api/marketdata/instruments/{id}</c> (T2.9's first internal, service-
/// to-service call in the solution). Resilience (retry+jitter, circuit breaker, timeout) and service
/// discovery come from <c>ConfigureHttpClientDefaults</c> in ServiceDefaults — nothing extra to wire
/// here (dotnet.md "HTTP between services").
/// </summary>
public sealed class MarketDataInstrumentLookupClient(HttpClient httpClient) : IInstrumentLookupClient
{
    public async Task<InstrumentLookupStatus> CheckAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"/api/marketdata/instruments/{instrumentId}", cancellationToken);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => InstrumentLookupStatus.Found,
                HttpStatusCode.NotFound => InstrumentLookupStatus.NotFound,
                _ => InstrumentLookupStatus.Unavailable,
            };
        }
        catch (OperationCanceledException)
        {
            // Must be checked first and unconditionally (a `when` filter here would let this fall
            // through to the broad catch below, which would wrongly swallow genuine caller
            // cancellation too). Only the resilience pipeline's own total-request timeout — which
            // cancels internally, not via our caller's token — maps to Unavailable; a caller-
            // initiated cancellation propagates as-is.
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return InstrumentLookupStatus.Unavailable;
        }
        catch (Exception)
        {
            // Every other failure talking to MarketData (connection refused, DNS, TLS, an open
            // circuit breaker, ...) collapses to the same fail-closed outcome (T2.9 AC): this is a
            // single, well-defined dependency boundary, so a 503 beats leaking transport details as
            // an unhandled 500.
            return InstrumentLookupStatus.Unavailable;
        }
    }
}
