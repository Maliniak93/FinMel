using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.ContributionLimits;

public static class ContributionLimitErrors
{
    public static Error Invalid { get; } =
        new("Validation.ContributionLimit", "A contribution limit needs an amount above 0 and a year from 2000 to 2100.");

    public static Error NotFound(int year) =>
        new("NotFound.ContributionLimit", $"There is no contribution limit for {year}.");
}
