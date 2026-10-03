namespace Skarbiec.MarketData.Data;

// Version drops a late or duplicate position event; IsRemoved is a terminal tombstone, since AssetRemoved carries no version.
public sealed class AssetInstrumentLink
{
    public required Guid AssetId { get; init; }

    public Guid? InstrumentId { get; set; }

    public long Version { get; set; }
    public bool IsRemoved { get; set; }
}
