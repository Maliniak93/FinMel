using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.RecurringIncomes;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.UpdateRecurringExpense;

public sealed record UpdateRecurringExpenseRequest : IValidatableObject
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    public required DateOnly StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    /// <summary>The month (yyyy-MM) the amount and rule apply from; later scheduled changes are kept.</summary>
    [Required]
    public required string EffectiveFrom { get; init; }

    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public required decimal Amount { get; init; }

    [Range(1, 12)]
    public required int IntervalMonths { get; init; }

    public required RecurrenceDayKind DayKind { get; init; }

    /// <summary>Required for the DayOfMonth kind, a day clamped to the month's length; absent for the last business day.</summary>
    public int? DayOfMonth { get; init; }

    public required BusinessDayShift Shift { get; init; }

    internal YearMonth EffectiveMonth => YearMonth.Parse(EffectiveFrom);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in RecurringIncomeValidation.Validate(DayKind, Shift, DayOfMonth, StartDate, EndDate))
        {
            yield return result;
        }

        if (!DateOnly.TryParseExact(EffectiveFrom, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || parsed < RecurringIncomeLimits.FirstDay
            || parsed > RecurringIncomeLimits.LastDay)
        {
            yield return new ValidationResult(
                $"The effective month must be between {RecurringIncomeLimits.FirstDay:yyyy-MM} and {RecurringIncomeLimits.LastDay:yyyy-MM} (yyyy-MM).",
                [nameof(EffectiveFrom)]);
        }
    }
}
