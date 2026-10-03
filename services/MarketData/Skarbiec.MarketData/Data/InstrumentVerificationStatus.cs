namespace Skarbiec.MarketData.Data;

// Verified must stay first (0): the column's HasDefaultValue(Verified) relies on EF treating the CLR default as unset.
public enum InstrumentVerificationStatus
{
    Verified,
    Unverified,
    Failed,
}
