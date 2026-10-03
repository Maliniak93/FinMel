using System.ComponentModel.DataAnnotations;

namespace Skarbiec.MarketData.Features.GetFxRatesBatch;

public sealed record FxRatesBatchRequest
{
    /// <summary>6-letter pair codes such as "USDPLN".</summary>
    [MinLength(1), MaxLength(1000)]
    public required IReadOnlyList<string> Pairs { get; init; }

    public required DateOnly AsOfDate { get; init; }
}
