using System.Net;

namespace Skarbiec.Portfolio.MarketData;

public sealed class MarketDataInstrumentLookupClient(HttpClient httpClient) : IInstrumentLookupClient
{
    public async Task<InstrumentLookupStatus> CheckAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync($"/internal/instruments/{instrumentId}", cancellationToken);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => InstrumentLookupStatus.Found,
                HttpStatusCode.NotFound => InstrumentLookupStatus.NotFound,
                _ => InstrumentLookupStatus.Unavailable,
            };
        }
        catch (OperationCanceledException)
        {
            // Checked first and unconditionally: only the resilience pipeline's own timeout maps to Unavailable; caller cancellation propagates.
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return InstrumentLookupStatus.Unavailable;
        }
        catch (Exception)
        {
            // Any other transport failure fails closed, so the caller answers 503 instead of leaking a 500.
            return InstrumentLookupStatus.Unavailable;
        }
    }
}
