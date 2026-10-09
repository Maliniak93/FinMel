using System.ComponentModel.DataAnnotations;

namespace Skarbiec.MarketData.Features.GetPricesHistoryBatch;

public sealed record PricesHistoryBatchRequest : IValidatableObject
{
    [MinLength(1), MaxLength(1000)]
    public required IReadOnlyList<Guid> InstrumentIds { get; init; }

    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (To < From)
        {
            yield return new ValidationResult("To must not be before From.", [nameof(To)]);
        }
    }
}
