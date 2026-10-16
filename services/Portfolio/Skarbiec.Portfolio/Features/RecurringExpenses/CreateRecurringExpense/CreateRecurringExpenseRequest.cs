using System.ComponentModel.DataAnnotations;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.RecurringIncomes;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.CreateRecurringExpense;

public sealed record CreateRecurringExpenseRequest : IValidatableObject
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public required decimal Amount { get; init; }

    public required DateOnly StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    [Range(1, 12)]
    public required int IntervalMonths { get; init; }

    public required RecurrenceDayKind DayKind { get; init; }

    /// <summary>Required for the DayOfMonth kind, a day clamped to the month's length; absent for the last business day.</summary>
    public int? DayOfMonth { get; init; }

    public required BusinessDayShift Shift { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        RecurringIncomeValidation.Validate(DayKind, Shift, DayOfMonth, StartDate, EndDate);
}
