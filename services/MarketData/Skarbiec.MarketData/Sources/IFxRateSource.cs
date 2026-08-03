namespace Skarbiec.MarketData.Sources;

/// <summary>
/// FX-specific counterpart to <see cref="IPriceSource"/>: NBP prices currency pairs (table A), not
/// <see cref="Data.Instrument"/> rows, so it fetches by ISO currency code rather than by instrument
/// (ADR-008 — PLN base currency). NBP is the only planned source for <see cref="Data.FxRate"/>, but
/// this still exists as its own interface — same reasoning as ADR-007's for <see cref="IPriceSource"/>
/// — so PriceSyncJob (T2.6) never depends on a concrete vendor client, and fixture-based tests can
/// swap the fetch the same way T2.2's kit does.
/// </summary>
public interface IFxRateSource
{
    /// <summary>Minimum delay the caller should hold between requests — see <see cref="IPriceSource.RequestDelay"/>.</summary>
    TimeSpan RequestDelay { get; }

    /// <summary>Latest available rate per requested currency code (quoted against PLN). A day with
    /// no trading (weekend/holiday) round-trips as <see cref="PriceFetchOutcome.NoData"/>, not an
    /// error.</summary>
    Task<PriceFetchResult<FxRateQuote>> FetchLatestAsync(
        IReadOnlyCollection<string> currencyCodes, CancellationToken cancellationToken);

    /// <summary>Historical rates for one currency code over <paramref name="from"/>..<paramref name="to"/>
    /// (inclusive), used for backfill (T2.7).</summary>
    Task<PriceFetchResult<FxRateQuote>> FetchHistoryAsync(
        string currencyCode, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
