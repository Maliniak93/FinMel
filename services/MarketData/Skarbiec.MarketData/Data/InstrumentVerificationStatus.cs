namespace Skarbiec.MarketData.Data;

// Only meaningful for custom instruments (Features/AddCustomInstrument, T2.8) — dictionary/seeded
// instruments are created straight into Verified (MarketDataSeeder) since they're curated by hand.
//
// Verified must stay the first (= 0 = CLR default) member: MarketDataDbContext configures a
// HasDefaultValue(Verified) column default, and EF Core's insert logic treats a property holding its
// CLR-default value as "unset", deferring to that DB default — so an explicit Unverified/Failed value
// (non-zero) always round-trips, while an explicit Verified value simply agrees with what the DB
// default would have produced anyway. Reordering this enum silently reintroduces the bug.
public enum InstrumentVerificationStatus
{
    Verified,
    Unverified,
    Failed,
}
