using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences.SettleOccurrence;

public sealed record SettleOccurrenceRequest : IValidatableObject
{
    /// <summary>What actually arrived, in the plan's currency.</summary>
    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public required decimal ActualAmount { get; init; }

    public required DateOnly ActualDate { get; init; }

    /// <summary>The account the money arrived on; same currency as the plan.</summary>
    public required Guid AccountId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ActualDate < RecurringIncomeLimits.FirstDay)
        {
            yield return new ValidationResult(
                $"The actual date can't be before {RecurringIncomeLimits.FirstDay:yyyy-MM-dd}.", [nameof(ActualDate)]);
        }
    }
}
