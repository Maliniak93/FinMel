namespace Skarbiec.MarketData.Features.GetFxRatesBatch;

/// <summary>Latest rate at or before <c>AsOfDate</c> for one pair; a pair absent from the response has no rate at all on or before it.</summary>
public sealed record FxRateResult
{
    public required string Pair { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal Rate { get; init; }
}

public sealed record FxRatesBatchResponse
{
    public required IReadOnlyList<FxRateResult> Rates { get; init; }
}
