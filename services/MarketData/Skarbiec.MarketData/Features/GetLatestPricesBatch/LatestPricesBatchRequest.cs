using System.ComponentModel.DataAnnotations;

namespace Skarbiec.MarketData.Features.GetLatestPricesBatch;

public sealed record LatestPricesBatchRequest
{
    [MinLength(1), MaxLength(1000)]
    public required IReadOnlyList<Guid> InstrumentIds { get; init; }

    public required DateOnly AsOfDate { get; init; }
}
