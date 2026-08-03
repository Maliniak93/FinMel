namespace Skarbiec.MarketData.Sources.CoinGecko;

/// <summary>
/// Thin HTTP fetch boundary for CoinGecko's public v3 API: returns the raw response body and
/// nothing else; all parsing lives in <see cref="CoinGeckoPriceSource"/>. A 429 (free-tier rate
/// limit) is translated to <see cref="CoinGeckoRateLimitedException"/> here rather than surfacing as
/// a generic transport failure, so the source can back off and retry instead of giving up outright
/// (T2.5 scope: "honor 429 with backoff"). Same "fetch-then-parse split" T2.2's <c>FixturePriceSource</c>
/// kit established, and T2.3/T2.4's <c>INbpApiClient</c>/<c>IStooqApiClient</c> followed.
/// </summary>
public interface ICoinGeckoApiClient
{
    /// <summary>Latest price for every id in one call (T2.5 scope: "batch by ids — one call for many
    /// coins"), keeping a multi-instrument sync from turning into one request per coin.</summary>
    Task<string> GetLatestAsync(IReadOnlyCollection<string> coinGeckoIds, CancellationToken cancellationToken);

    /// <summary>Historical prices for one coin over <paramref name="from"/>..<paramref name="to"/>
    /// (inclusive), used for backfill (T2.7). Per-coin — CoinGecko's market-chart endpoint takes a
    /// single id.</summary>
    Task<string> GetHistoryAsync(string coinGeckoId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
