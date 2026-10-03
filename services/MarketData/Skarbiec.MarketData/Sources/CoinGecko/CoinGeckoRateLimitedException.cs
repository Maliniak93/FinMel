namespace Skarbiec.MarketData.Sources.CoinGecko;

// The free tier answered 429 with Retry-After: 54 after a handful of quick calls, so the limit is hit in normal use.
public sealed class CoinGeckoRateLimitedException(TimeSpan? retryAfter) : Exception(
    retryAfter is null
        ? "CoinGecko rate limit (429 Too Many Requests) with no Retry-After header."
        : $"CoinGecko rate limit (429 Too Many Requests); Retry-After {retryAfter}.")
{
    // For a 429 without the header, so callers always have a concrete wait.
    public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(60);

    public TimeSpan RetryAfter { get; } = retryAfter ?? DefaultRetryAfter;
}
