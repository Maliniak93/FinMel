using System.Net;

namespace Skarbiec.MarketData.Sources.CoinGecko;

/// <summary>
/// Real HTTP implementation of <see cref="ICoinGeckoApiClient"/> — a typed client resolved through
/// <see cref="IHttpClientFactory"/> (registered by
/// <see cref="CoinGeckoSourceExtensions.AddCoinGeckoSource{TBuilder}"/>), so ServiceDefaults' standard
/// resilience handler (retry+jitter, circuit breaker, timeout) applies automatically; no per-source
/// wiring needed (see <see cref="IPriceSource"/>'s doc comment). Prices are always requested in USD —
/// a stable quote currency documented once here rather than per instrument; conversion to PLN happens
/// at valuation time via <c>FxRate</c>, same as any other foreign-quoted instrument (ADR-008).
/// </summary>
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

    // 429 carries the Retry-After the source needs to back off correctly (see
    // CoinGeckoRateLimitedException's doc comment) — translated here rather than left to throw via
    // EnsureSuccessStatusCode, which would discard the header. Any other non-success status still
    // throws via EnsureSuccessStatusCode, treated as a transport failure by the source (same as
    // NbpApiClient/StooqApiClient).
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
