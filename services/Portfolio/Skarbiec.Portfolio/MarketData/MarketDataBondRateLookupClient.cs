using System.Net;
using System.Net.Http.Json;

namespace Skarbiec.Portfolio.MarketData;

public sealed class MarketDataBondRateLookupClient(HttpClient httpClient) : IBondRateLookupClient
{
    public async Task<BondRateLookupResult> GetRatesAsync(IReadOnlyCollection<string> seriesCodes, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "/internal/bond-series/rates-batch", new RatesBatchRequest(seriesCodes), cancellationToken);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return BondRateLookupResult.Unavailable;
            }

            var body = await response.Content.ReadFromJsonAsync<RatesBatchResponse>(cancellationToken);
            if (body is null)
            {
                return BondRateLookupResult.Unavailable;
            }

            // MarketData counts periods from 0; Portfolio's BondPeriod.Index counts from 1.
            var rates = body.Series.ToDictionary(
                s => s.Code,
                IReadOnlyDictionary<int, decimal> (s) => s.PeriodRates.ToDictionary(r => r.PeriodIndex + 1, r => r.RatePercent),
                StringComparer.OrdinalIgnoreCase);

            return BondRateLookupResult.Found(rates);
        }
        catch (OperationCanceledException)
        {
            // Checked first and unconditionally: only the resilience pipeline's own timeout maps to Unavailable; caller cancellation propagates.
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return BondRateLookupResult.Unavailable;
        }
        catch (Exception)
        {
            // Any other failure talking to MarketData fails soft: the caller shows the bond without an estimate.
            return BondRateLookupResult.Unavailable;
        }
    }

    private sealed record RatesBatchRequest(IReadOnlyCollection<string> Codes);

    private sealed record PeriodRate(int PeriodIndex, decimal RatePercent);

    private sealed record SeriesRates(string Code, IReadOnlyList<PeriodRate> PeriodRates);

    private sealed record RatesBatchResponse(IReadOnlyList<SeriesRates> Series);
}
