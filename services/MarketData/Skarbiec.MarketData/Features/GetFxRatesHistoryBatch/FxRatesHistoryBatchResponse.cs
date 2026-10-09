namespace Skarbiec.MarketData.Features.GetFxRatesHistoryBatch;

public sealed record HistoryRate
{
    public required DateOnly Date { get; init; }
    public required decimal Rate { get; init; }
}

/// <summary>Rates ascending: the latest one before From (if any), then every rate in From..To.</summary>
public sealed record FxRateSeries
{
    public required string Pair { get; init; }
    public required IReadOnlyList<HistoryRate> Rates { get; init; }
}

public sealed record FxRatesHistoryBatchResponse
{
    public required IReadOnlyList<FxRateSeries> Series { get; init; }
}
