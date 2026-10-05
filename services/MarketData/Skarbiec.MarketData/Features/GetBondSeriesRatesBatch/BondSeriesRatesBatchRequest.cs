using System.ComponentModel.DataAnnotations;

namespace Skarbiec.MarketData.Features.GetBondSeriesRatesBatch;

public sealed record BondSeriesRatesBatchRequest
{
    [MinLength(1), MaxLength(1000)]
    public required IReadOnlyList<string> Codes { get; init; }
}
