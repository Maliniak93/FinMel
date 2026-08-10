using System.ComponentModel.DataAnnotations;

namespace Skarbiec.MarketData.Features.GetFxRatesBatch;

public sealed record FxRatesBatchRequest
{
    /// <summary>6-letter pair codes (e.g. "USDPLN") — NBP table A convention, ADR-008.</summary>
    [MinLength(1), MaxLength(1000)]
    public required IReadOnlyList<string> Pairs { get; init; }

    public required DateOnly AsOfDate { get; init; }
}
