using System.ComponentModel.DataAnnotations;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.RecurringIncomes;

internal static class RecurringIncomeValidation
{
    public static IEnumerable<ValidationResult> Validate(
        RecurrenceDayKind dayKind, BusinessDayShift shift, int? dayOfMonth, DateOnly startDate, DateOnly? endDate)
    {
        if (!Enum.IsDefined(dayKind))
        {
            yield return new ValidationResult("Unknown day kind.", ["DayKind"]);
        }

        if (!Enum.IsDefined(shift))
        {
            yield return new ValidationResult("Unknown shift.", ["Shift"]);
        }

        if (dayKind == RecurrenceDayKind.DayOfMonth && dayOfMonth is not (>= 1 and <= 31))
        {
            yield return new ValidationResult("A day of month from 1 to 31 is required.", ["DayOfMonth"]);
        }

        if (dayKind == RecurrenceDayKind.LastBusinessDay && dayOfMonth is not null)
        {
            yield return new ValidationResult("The last business day has no day of month.", ["DayOfMonth"]);
        }

        if (dayKind == RecurrenceDayKind.LastBusinessDay && shift != BusinessDayShift.None)
        {
            yield return new ValidationResult("The last business day takes no shift.", ["Shift"]);
        }

        if (startDate < RecurringIncomeLimits.FirstDay || startDate > RecurringIncomeLimits.LastDay)
        {
            yield return new ValidationResult(
                $"The start date must be between {RecurringIncomeLimits.FirstDay:yyyy-MM-dd} and {RecurringIncomeLimits.LastDay:yyyy-MM-dd}.",
                ["StartDate"]);
        }

        if (endDate is { } end && (end < startDate || end > RecurringIncomeLimits.LastDay))
        {
            yield return new ValidationResult(
                $"The end date must be on or after the start date and not after {RecurringIncomeLimits.LastDay:yyyy-MM-dd}.",
                ["EndDate"]);
        }
    }
}
