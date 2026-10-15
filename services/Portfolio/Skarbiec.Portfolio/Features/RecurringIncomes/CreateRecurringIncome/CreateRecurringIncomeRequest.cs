using System.ComponentModel.DataAnnotations;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.CreateRecurringIncome;

public sealed record CreateRecurringIncomeRequest : IValidatableObject
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public required decimal Amount { get; init; }

    public required Guid AccountId { get; init; }

    public required DateOnly StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    [Range(1, 12)]
    public required int IntervalMonths { get; init; }

    public required RecurrenceDayKind DayKind { get; init; }

    /// <summary>Required for the DayOfMonth kind, a day clamped to the month's length; absent for the last business day.</summary>
    public int? DayOfMonth { get; init; }

    public required BusinessDayShift Shift { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(DayKind))
        {
            yield return new ValidationResult("Unknown day kind.", [nameof(DayKind)]);
        }

        if (!Enum.IsDefined(Shift))
        {
            yield return new ValidationResult("Unknown shift.", [nameof(Shift)]);
        }

        if (DayKind == RecurrenceDayKind.DayOfMonth && DayOfMonth is not (>= 1 and <= 31))
        {
            yield return new ValidationResult("A day of month from 1 to 31 is required.", [nameof(DayOfMonth)]);
        }

        if (DayKind == RecurrenceDayKind.LastBusinessDay && DayOfMonth is not null)
        {
            yield return new ValidationResult("The last business day has no day of month.", [nameof(DayOfMonth)]);
        }

        if (DayKind == RecurrenceDayKind.LastBusinessDay && Shift != BusinessDayShift.None)
        {
            yield return new ValidationResult("The last business day takes no shift.", [nameof(Shift)]);
        }

        if (StartDate < RecurringIncomeLimits.FirstDay || StartDate > RecurringIncomeLimits.LastDay)
        {
            yield return new ValidationResult(
                $"The start date must be between {RecurringIncomeLimits.FirstDay:yyyy-MM-dd} and {RecurringIncomeLimits.LastDay:yyyy-MM-dd}.",
                [nameof(StartDate)]);
        }

        if (EndDate is { } end && (end < StartDate || end > RecurringIncomeLimits.LastDay))
        {
            yield return new ValidationResult(
                $"The end date must be on or after the start date and not after {RecurringIncomeLimits.LastDay:yyyy-MM-dd}.",
                [nameof(EndDate)]);
        }
    }
}
