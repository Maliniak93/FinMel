namespace Skarbiec.MarketData.Sources.Nbp;

/// <summary>
/// Thin HTTP fetch boundary for the NBP web API (api.nbp.pl): returns the raw JSON body, or
/// <see langword="null"/> for a 404 — NBP's signal for "no data published" on a non-trading day —
/// and nothing else; all parsing lives in <see cref="NbpPriceSource"/>/<see cref="NbpFxRateSource"/>.
/// An interface (not just <see cref="NbpApiClient"/> directly) so tests can swap in canned recorded
/// responses and exercise the real sources' parse logic through their public
/// <see cref="IPriceSource"/>/<see cref="IFxRateSource"/> methods — the same "fetch-then-parse
/// split" T2.2's <c>FixturePriceSource</c> kit established.
/// </summary>
public interface INbpApiClient
{
    /// <summary>Table A for one day, or the latest published table when <paramref name="date"/> is <see langword="null"/>.</summary>
    Task<string?> GetTableAAsync(DateOnly? date, CancellationToken cancellationToken);

    /// <summary>Table A for every business day in <paramref name="from"/>..<paramref name="to"/> (inclusive) — caller must respect NBP's per-request range limit (<see cref="NbpFxRateSource"/>).</summary>
    Task<string?> GetTableARangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Gold price for one day, or the latest published price when <paramref name="date"/> is <see langword="null"/>.</summary>
    Task<string?> GetGoldPriceAsync(DateOnly? date, CancellationToken cancellationToken);

    /// <summary>Gold price for every business day in <paramref name="from"/>..<paramref name="to"/> (inclusive) — caller must respect NBP's per-request range limit (<see cref="NbpPriceSource"/>).</summary>
    Task<string?> GetGoldPriceRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
