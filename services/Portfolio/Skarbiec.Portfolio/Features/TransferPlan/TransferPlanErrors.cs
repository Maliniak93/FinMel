using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.TransferPlan;

public static class TransferPlanErrors
{
    public static Error InvalidMinimumTransfer { get; } =
        new("Validation.InvalidMinimumTransfer", "The minimum transfer must be a whole number of złoty from 0 to 1 000 000.");

    public static Error NotPreview(string month) =>
        new("Conflict.TransferPlanNotPreview", $"The transfer plan for {month} is not a preview, so it can't be accepted or skipped.");

    public static Error RowChecked(string month) =>
        new("Conflict.TransferPlanRowChecked", $"A row of the transfer plan for {month} is checked — uncheck it before undoing.");

    public static Error NotFound(string month) =>
        new("NotFound.TransferPlan", $"There is no accepted or skipped transfer plan for {month}.");

    public static Error RowNotFound(Guid rowId) =>
        new("NotFound.TransferPlanRow", $"Transfer plan row '{rowId}' was not found.");
}
