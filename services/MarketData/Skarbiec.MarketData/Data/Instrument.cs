using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Data;

// Global reference data shared across users, not user-owned — no IUserOwned/UserId (see
// MarketDataDbContext). Custom per-user instruments are handled separately in T2.8.
public sealed class Instrument
{
    public required Guid Id { get; init; }
    public required string Ticker { get; set; }
    public required string Name { get; set; }
    public required PriceSource Source { get; set; }
    public required string QuoteCurrency { get; set; }
    public required AssetClass AssetClass { get; set; }

    // Defaults to Verified so every pre-T2.8 call site (seeder, T2.6/T2.7 test fixtures) keeps
    // compiling without stating it; only AddCustomInstrument (T2.8) explicitly starts an instrument
    // as Unverified, pending the one-off HistoryBackfillJob run that resolves it one way or the other.
    public InstrumentVerificationStatus VerificationStatus { get; set; } = InstrumentVerificationStatus.Verified;
}
