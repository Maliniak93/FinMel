namespace Skarbiec.MarketData.Sources.Stooq;

/// <summary>
/// Thin HTTP fetch boundary for stooq.com's CSV endpoints: returns the raw response body and
/// nothing else; all parsing lives in <see cref="StooqPriceSource"/>. Unlike NBP, Stooq signals
/// "no data" inside a 200 OK body (an "N/D"-valued row for a latest quote, a literal "No data" line
/// for a history range) rather than via HTTP status, so there is no null/404 translation here — see
/// <see cref="StooqApiClient"/>'s doc comment. An interface (not just <see cref="StooqApiClient"/>
/// directly) so tests can swap in canned recorded responses and exercise the real source's parse
/// logic through its public <see cref="IPriceSource"/> methods — the same "fetch-then-parse split"
/// T2.2's <c>FixturePriceSource</c> kit established, and T2.3's <c>INbpApiClient</c> followed.
/// </summary>
public interface IStooqApiClient
{
    /// <summary>Latest quote CSV row for one ticker (per-ticker fetch — T2.4 scope: "one bad ticker
    /// must not fail the batch"; isolation falls naturally out of each ticker being its own
    /// independent request).</summary>
    Task<string> GetLatestAsync(string ticker, CancellationToken cancellationToken);

    /// <summary>Daily historical CSV for one ticker over <paramref name="from"/>..<paramref name="to"/>
    /// (inclusive), used for backfill (T2.7).</summary>
    Task<string> GetHistoryAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
