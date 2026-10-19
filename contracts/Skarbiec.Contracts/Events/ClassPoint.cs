namespace Skarbiec.Contracts.Events;

/// <summary>The class an account's cash counts under from Date on, until the next point.</summary>
public sealed record ClassPoint
{
    public required DateOnly Date { get; init; }
    public required AssetClass AssetClass { get; init; }
}
