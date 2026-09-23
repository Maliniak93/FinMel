namespace Skarbiec.MarketData.Data;

/// <summary>
/// Which instrument (if any) one Portfolio asset currently points at, as last reported by
/// <c>AssetPositionChanged</c>/<c>AssetRemoved</c> (spec-04 design decisions 9-10). This is what makes
/// the usage consumers idempotent and order-safe: <see cref="Version"/> drops a late or duplicate
/// position event, and <see cref="IsRemoved"/> is a terminal tombstone (<c>AssetRemoved</c> carries no
/// version), so anything arriving for a removed asset is a no-op. <see cref="InstrumentUsage"/> is
/// derived from these rows.
/// </summary>
public sealed class AssetInstrumentLink
{
    public required Guid AssetId { get; init; }

    /// <summary>Null when the asset is not market-valued, or once it is removed.</summary>
    public Guid? InstrumentId { get; set; }

    public long Version { get; set; }
    public bool IsRemoved { get; set; }
}
