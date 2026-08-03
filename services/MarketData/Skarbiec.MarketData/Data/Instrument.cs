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
}
