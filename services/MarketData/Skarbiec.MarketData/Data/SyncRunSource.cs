namespace Skarbiec.MarketData.Data;

// Schedule must stay first (0): the column's HasDefaultValue(Schedule) relies on it.
public enum SyncRunSource
{
    Schedule,
    Startup,
    Manual,
}
