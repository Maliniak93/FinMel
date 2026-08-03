using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

/// <summary>
/// Isolates one external price API (NBP, Stooq, CoinGecko) so PriceSyncJob (T2.6) depends only on
/// this interface, never a concrete vendor client (ADR-007; roadmap risk "Stooq/CoinGecko changes
/// its format"). Implementations own their own <c>HttpClient</c> and parsing; ServiceDefaults'
/// standard resilience handler (retry+jitter, circuit breaker, timeout) applies automatically to
/// any typed/named client resolved through <c>IHttpClientFactory</c> — no per-source wiring needed.
/// </summary>
public interface IPriceSource
{
    /// <summary>
    /// Which <see cref="Instrument.Source"/> this implementation serves — the capability check the
    /// job uses to route an instrument to the right source. Callers are expected to pass only
    /// instruments whose <see cref="Instrument.Source"/> matches; a source does not filter itself.
    /// </summary>
    PriceSource Source { get; }

    /// <summary>
    /// Minimum delay the caller should hold between requests to this source — rate-limit
    /// friendliness (e.g. the CoinGecko free tier); <see cref="TimeSpan.Zero"/> for sources without
    /// a limit. Batching itself is the caller's concern: passing more instruments per call is how a
    /// source is asked to batch.
    /// </summary>
    TimeSpan RequestDelay { get; }

    /// <summary>Latest available quote per instrument. A day with no trading (weekend/holiday)
    /// round-trips as <see cref="PriceFetchOutcome.NoData"/>, not an error.</summary>
    Task<PriceFetchResult<InstrumentQuote>> FetchLatestAsync(
        IReadOnlyCollection<Instrument> instruments, CancellationToken cancellationToken);

    /// <summary>Historical quotes for one instrument over <paramref name="from"/>..<paramref name="to"/>
    /// (inclusive), used for backfill (T2.7).</summary>
    Task<PriceFetchResult<InstrumentQuote>> FetchHistoryAsync(
        Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
