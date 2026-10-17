using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.TransferPlan;

public static class TransferPlanErrors
{
    public static Error InvalidMinimumTransfer { get; } =
        new("Validation.InvalidMinimumTransfer", "The minimum transfer must be a whole number of złoty from 0 to 1 000 000.");
}
