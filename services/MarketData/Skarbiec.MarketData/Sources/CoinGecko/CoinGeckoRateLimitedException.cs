namespace Skarbiec.MarketData.Sources.CoinGecko;

/// <summary>
/// Thrown by <see cref="ICoinGeckoApiClient"/> when CoinGecko responds 429 Too Many Requests,
/// carrying the <c>Retry-After</c> duration so <see cref="CoinGeckoPriceSource"/> can back off by
/// exactly that long instead of hammering the free tier (T2.5 scope: "honor 429 with backoff...
/// never hammer"). Live check against api.coingecko.com on 2026-08-03: the public
/// <c>market_chart/range</c> endpoint returned 429 after only a handful of calls in quick
/// succession, with <c>Retry-After: 54</c> — confirming the free tier's rate limit is tight enough
/// to hit in normal use, not just a theoretical risk.
/// </summary>
public sealed class CoinGeckoRateLimitedException(TimeSpan? retryAfter) : Exception(
    retryAfter is null
        ? "CoinGecko rate limit (429 Too Many Requests) with no Retry-After header."
        : $"CoinGecko rate limit (429 Too Many Requests); Retry-After {retryAfter}.")
{
    /// <summary>Falls back to a conservative default when CoinGecko omits the header (not observed
    /// live, but not guaranteed either) so callers always have a concrete duration to wait.</summary>
    public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(60);

    public TimeSpan RetryAfter { get; } = retryAfter ?? DefaultRetryAfter;
}
