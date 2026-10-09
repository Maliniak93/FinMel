using System.ComponentModel.DataAnnotations;

namespace Skarbiec.MarketData.Features.GetFxRatesHistoryBatch;

public sealed record FxRatesHistoryBatchRequest : IValidatableObject
{
    /// <summary>6-letter pair codes such as "USDPLN".</summary>
    [MinLength(1), MaxLength(1000)]
    public required IReadOnlyList<string> Pairs { get; init; }

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
