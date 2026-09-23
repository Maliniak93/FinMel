namespace Skarbiec.MarketData.Data;

/// <summary>
/// Which job wrote a <see cref="SyncRun"/> (spec-04 design decision 7) — all three MarketData jobs
/// share one run log. <see cref="Prices"/> must stay the first member (value 0): the column's
/// <c>HasDefaultValue(Prices)</c> relies on it, and every row that predates this column was a price sync.
/// </summary>
public enum SyncRunKind
{
    Prices,
    Fx,
    Backfill,
}
