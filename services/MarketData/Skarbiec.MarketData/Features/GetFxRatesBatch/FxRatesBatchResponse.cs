namespace Skarbiec.MarketData.Features.GetFxRatesBatch;

/// <summary>Latest rate on or before AsOfDate; a pair absent from the response has none.</summary>
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
