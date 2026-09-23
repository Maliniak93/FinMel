namespace Skarbiec.MarketData.Data;

/// <summary>
/// How many live Portfolio assets point at an instrument (spec-04 design decision 9) — the "in use"
/// set <see cref="Sources.PriceSyncJob"/> filters by. Derived, never incremented by hand: the usage
/// consumers recompute <see cref="AssetCount"/> from <see cref="AssetInstrumentLink"/> rows, so a
/// redelivered, out-of-order or instrument-switching event always converges on the same number.
/// <see cref="InstrumentId"/> is a plain id, no FK — an event can name an instrument before this
/// service has ever seen it.
/// </summary>
public sealed class InstrumentUsage
{
    public required Guid InstrumentId { get; init; }
    public int AssetCount { get; set; }

    /// <summary>Stamped once, when the row is created; a detach-then-reattach never moves it.</summary>
    public required DateTimeOffset FirstUsedAt { get; init; }
}
