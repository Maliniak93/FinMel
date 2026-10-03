using System.Net;

namespace Skarbiec.MarketData.Sources.CoinGecko;

// Prices are always requested in USD; conversion to PLN happens at valuation time.
public sealed class CoinGeckoApiClient(HttpClient httpClient) : ICoinGeckoApiClient
{
    private const string VsCurrency = "usd";

    public Task<string> GetLatestAsync(IReadOnlyCollection<string> coinGeckoIds, CancellationToken cancellationToken)
    {
        var ids = Uri.EscapeDataString(string.Join(',', coinGeckoIds));
        return GetRawAsync($"simple/price?ids={ids}&vs_currencies={VsCurrency}&include_last_updated_at=true", cancellationToken);
    }

    public Task<string> GetHistoryAsync(string coinGeckoId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var id = Uri.EscapeDataString(coinGeckoId);
        return GetRawAsync(
            $"coins/{id}/market_chart/range?vs_currency={VsCurrency}&from={ToUnixSeconds(from)}&to={ToUnixSeconds(to)}",
            cancellationToken);
    }

    // A 429 becomes CoinGeckoRateLimitedException here, since EnsureSuccessStatusCode would discard Retry-After.
    private async Task<string> GetRawAsync(string requestUri, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(requestUri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new CoinGeckoRateLimitedException(response.Headers.RetryAfter?.Delta);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static long ToUnixSeconds(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
}
