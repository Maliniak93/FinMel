using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.EndRecurringIncome;

public sealed record EndRecurringIncomeRequest : IValidatableObject
{
    public required DateOnly EndDate { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate > RecurringIncomeLimits.LastDay)
        {
            yield return new ValidationResult(
                $"The end date can't be after {RecurringIncomeLimits.LastDay:yyyy-MM-dd}.", [nameof(EndDate)]);
        }
    }
}
