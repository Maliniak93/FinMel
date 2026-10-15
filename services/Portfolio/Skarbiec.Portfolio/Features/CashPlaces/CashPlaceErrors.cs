using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.CashPlaces;

internal static class CashPlaceErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.CashPlace", $"Cash place '{id}' was not found.");

    public static Error DuplicateName(string name) =>
        new("Conflict.DuplicateName", $"A cash place named '{name}' already exists.");

    public static Error DuplicateCurrency(string currency) =>
        new("Conflict.DuplicateCurrency", $"The cash place already holds a {currency} balance.");

    public static Error Archived(Guid id) =>
        new("Conflict.CashPlaceArchived", $"Cash place '{id}' is archived — restore it to make changes.");

    public static Error NotEmpty(Guid id) =>
        new("Conflict.CashPlaceNotEmpty", $"Cash place '{id}' still holds money.");

    public static Error HasHistory(Guid id) =>
        new("Conflict.CashPlaceHasHistory", $"Cash place '{id}' has transactions, so it can't be deleted.");

    public static Error Managed(Guid assetId) =>
        new("Conflict.CashPlaceManaged", $"Asset '{assetId}' is a cash place's balance — change it through the place, its deposits, withdrawals and transfers.");

    public static Error UseCashPlaceEndpoints { get; } =
        new("Validation.UseCashPlaceEndpoints", "A Cash asset is a cash place's balance — create a cash place instead.");
}
