using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Planning;

internal static class PlanningErrors
{
    public static Error InvalidMonth(string month) =>
        new("Validation.InvalidMonth", $"'{month}' is not a month between 2011-01 and 2099-12 (yyyy-MM).");
}
