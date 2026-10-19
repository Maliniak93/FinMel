using Skarbiec.Contracts;

namespace Skarbiec.Reporting.Data;

/// <summary>The class the position is valued under from Date on, until the next point.</summary>
public sealed record PositionClassPoint
{
    public required DateOnly Date { get; init; }
    public required AssetClass AssetClass { get; init; }
}
