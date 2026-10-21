using System.ComponentModel.DataAnnotations;
using Skarbiec.Portfolio.Features.RecurringIncomes;

namespace Skarbiec.Portfolio.Features.OneOffIncomes;

internal static class OneOffValidation
{
    public static IEnumerable<ValidationResult> Validate(DateOnly date)
    {
        if (date < RecurringIncomeLimits.FirstDay || date > RecurringIncomeLimits.LastDay)
        {
            yield return new ValidationResult(
                $"The date must be between {RecurringIncomeLimits.FirstDay:yyyy-MM-dd} and {RecurringIncomeLimits.LastDay:yyyy-MM-dd}.",
                ["Date"]);
        }
    }
}
