using System.ComponentModel.DataAnnotations;
using Skarbiec.Portfolio.Features.RecurringIncomes;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.EndRecurringExpense;

public sealed record EndRecurringExpenseRequest : IValidatableObject
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
