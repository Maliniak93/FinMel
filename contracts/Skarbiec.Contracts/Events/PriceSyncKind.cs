namespace Skarbiec.Contracts.Events;

/// <summary>
/// Which MarketData job finished the run a <see cref="DailyPricesSynced"/> reports (spec-04 design
/// decision 8). Deliberately narrower than MarketData's own <c>SyncRunKind</c>: a per-instrument
/// history backfill publishes nothing, so it has no member here. Both kinds trigger the same Reporting
/// snapshot recompute. <see cref="Prices"/> is the zero value, so a payload that omits the field reads
/// as a price sync.
/// </summary>
public enum PriceSyncKind
{
    Prices = 0,
    Fx = 1,
}
