using System.ComponentModel.DataAnnotations;

namespace Skarbiec.MarketData.Features.GetInstrumentsBatch;

public sealed record InstrumentsBatchRequest
{
    [MinLength(1), MaxLength(1000)]
    public required IReadOnlyList<Guid> InstrumentIds { get; init; }
}
