namespace Skarbiec.MarketData.Sources.Nbp;

// Returns null for a 404, NBP's signal that nothing was published.
public interface INbpApiClient
{
    Task<string?> GetTableAAsync(DateOnly? date, CancellationToken cancellationToken);

    // The caller respects NBP's per-request range limit.
    Task<string?> GetTableARangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<string?> GetGoldPriceAsync(DateOnly? date, CancellationToken cancellationToken);

    // The caller respects NBP's per-request range limit.
    Task<string?> GetGoldPriceRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
