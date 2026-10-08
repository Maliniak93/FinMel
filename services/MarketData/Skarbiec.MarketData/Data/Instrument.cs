using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Data;

public sealed class Instrument
{
    public required Guid Id { get; init; }
    public required string Ticker { get; set; }
    public required string Name { get; set; }
    public required PriceSource Source { get; set; }
    public required string QuoteCurrency { get; set; }
    public required AssetClass AssetClass { get; set; }

    // The configured exchange name (GPW, Xetra) of a Stock/Etf listing; null for crypto and metals.
    public string? Exchange { get; set; }

    // Verified by default: only AddCustomInstrument starts an instrument Unverified.
    public InstrumentVerificationStatus VerificationStatus { get; set; } = InstrumentVerificationStatus.Verified;
}
