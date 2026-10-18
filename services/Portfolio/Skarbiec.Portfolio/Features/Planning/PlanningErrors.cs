using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Planning;

internal static class PlanningErrors
{
    public static Error InvalidMonth(string month) =>
        new("Validation.InvalidMonth", $"'{month}' is not a month between 2011-01 and 2099-12 (yyyy-MM).");

    public static Error InvalidYear(int year) =>
        new("Validation.InvalidYear", $"{year} is outside the years the plan can be viewed for.");

    public static Error InvalidPaymentLeadDays { get; } =
        new("Validation.InvalidPaymentLeadDays", "The payment lead time must be a whole number of days from 0 to 90.");
}
