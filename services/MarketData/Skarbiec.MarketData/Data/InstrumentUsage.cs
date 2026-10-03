namespace Skarbiec.MarketData.Data;

// Recounted from AssetInstrumentLink rows, never incremented, so redelivered or out-of-order events converge.
public sealed class InstrumentUsage
{
    public required Guid InstrumentId { get; init; }
    public int AssetCount { get; set; }

    public required DateTimeOffset FirstUsedAt { get; init; }
}
