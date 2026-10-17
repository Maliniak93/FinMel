namespace Skarbiec.Contracts.Events;

public sealed record LatestValuationChanged
{
    public required Guid UserId { get; init; }

    /// <summary>The date of the user's newest snapshot.</summary>
    public required DateOnly Date { get; init; }

    public required bool IsStale { get; init; }

    /// <summary>Net worth of that snapshot in PLN.</summary>
    public required decimal TotalPln { get; init; }

    /// <summary>The snapshot's lines summed per asset class in PLN; classes with 0 are omitted.</summary>
    public required IReadOnlyList<AssetClassValue> ByAssetClass { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed record AssetClassValue
{
    public required AssetClass AssetClass { get; init; }
    public required decimal ValuePln { get; init; }
}
