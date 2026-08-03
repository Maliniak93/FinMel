using Skarbiec.MarketData.Sources.CoinGecko;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

/// <summary>
/// Canned <see cref="ICoinGeckoApiClient"/>, keyed by call type — the same "swap the fetch, exercise
/// the real parse logic through the public interface" pattern <see cref="FakeNbpApiClient"/>/
/// <see cref="FakeStooqApiClient"/> established, adapted for CoinGecko's batched-latest /
/// per-coin-history shape (T2.5 scope). Also simulates a 429 (once, or on every call) so
/// <see cref="CoinGeckoPriceSource"/>'s retry-after backoff is testable without a real rate limit.
/// </summary>
public sealed class FakeCoinGeckoApiClient : ICoinGeckoApiClient
{
    private readonly Dictionary<string, string> _historyResponses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _throwOnHistory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CoinGeckoRateLimitedException> _rateLimitOnceOnHistory = new(StringComparer.OrdinalIgnoreCase);

    private string? _latestResponse;
    private Exception? _throwOnLatest;
    private CoinGeckoRateLimitedException? _rateLimitOnceOnLatest;
    private CoinGeckoRateLimitedException? _alwaysRateLimitOnLatest;

    public int LatestRequestCount { get; private set; }
    public int HistoryRequestCount { get; private set; }
    public IReadOnlyList<string>? LastRequestedIds { get; private set; }

    public FakeCoinGeckoApiClient WithLatestResponse(string rawResponse)
    {
        _latestResponse = rawResponse;
        return this;
    }

    public FakeCoinGeckoApiClient ThrowingOnLatest(Exception exception)
    {
        _throwOnLatest = exception;
        return this;
    }

    /// <summary>First call throws a 429 with <paramref name="retryAfter"/>; the retry (second call)
    /// returns <paramref name="rawResponse"/> — the "recovers after one backoff" case.</summary>
    public FakeCoinGeckoApiClient RateLimitedOnceThenLatest(TimeSpan retryAfter, string rawResponse)
    {
        _rateLimitOnceOnLatest = new CoinGeckoRateLimitedException(retryAfter);
        _latestResponse = rawResponse;
        return this;
    }

    /// <summary>Every call throws a 429 — the "still rate-limited after the one retry" case.</summary>
    public FakeCoinGeckoApiClient AlwaysRateLimitedOnLatest(TimeSpan retryAfter)
    {
        _alwaysRateLimitOnLatest = new CoinGeckoRateLimitedException(retryAfter);
        return this;
    }

    public FakeCoinGeckoApiClient WithHistoryResponse(string coinGeckoId, string rawResponse)
    {
        _historyResponses[coinGeckoId] = rawResponse;
        return this;
    }

    public FakeCoinGeckoApiClient ThrowingOnHistory(string coinGeckoId, Exception exception)
    {
        _throwOnHistory[coinGeckoId] = exception;
        return this;
    }

    public FakeCoinGeckoApiClient RateLimitedOnceThenHistory(string coinGeckoId, TimeSpan retryAfter, string rawResponse)
    {
        _rateLimitOnceOnHistory[coinGeckoId] = new CoinGeckoRateLimitedException(retryAfter);
        _historyResponses[coinGeckoId] = rawResponse;
        return this;
    }

    public Task<string> GetLatestAsync(IReadOnlyCollection<string> coinGeckoIds, CancellationToken cancellationToken)
    {
        LatestRequestCount++;
        LastRequestedIds = coinGeckoIds.ToArray();

        if (_alwaysRateLimitOnLatest is not null)
        {
            return Task.FromException<string>(_alwaysRateLimitOnLatest);
        }

        if (_rateLimitOnceOnLatest is not null)
        {
            var exception = _rateLimitOnceOnLatest;
            _rateLimitOnceOnLatest = null;
            return Task.FromException<string>(exception);
        }

        if (_throwOnLatest is not null)
        {
            return Task.FromException<string>(_throwOnLatest);
        }

        return _latestResponse is not null
            ? Task.FromResult(_latestResponse)
            : throw new InvalidOperationException("FakeCoinGeckoApiClient: no canned latest response wired.");
    }

    public Task<string> GetHistoryAsync(string coinGeckoId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        HistoryRequestCount++;

        if (_rateLimitOnceOnHistory.TryGetValue(coinGeckoId, out var rateLimitException))
        {
            _rateLimitOnceOnHistory.Remove(coinGeckoId);
            return Task.FromException<string>(rateLimitException);
        }

        if (_throwOnHistory.TryGetValue(coinGeckoId, out var exception))
        {
            return Task.FromException<string>(exception);
        }

        if (_historyResponses.TryGetValue(coinGeckoId, out var raw))
        {
            return Task.FromResult(raw);
        }

        throw new InvalidOperationException($"FakeCoinGeckoApiClient: no canned history response wired for id '{coinGeckoId}'.");
    }
}
