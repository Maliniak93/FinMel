namespace Skarbiec.MarketData.Data;

// Prices must stay first (0): the column's HasDefaultValue(Prices) relies on it.
public enum SyncRunKind
{
    Prices,
    Fx,
    Backfill,
    BondCatalog,
}
