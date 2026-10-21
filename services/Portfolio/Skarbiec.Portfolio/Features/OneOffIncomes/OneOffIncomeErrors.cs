using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.OneOffIncomes;

internal static class OneOffIncomeErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.OneOffIncome", $"One-off income '{id}' was not found.");

    public static Error NotSettled(Guid id) =>
        new("NotFound.OneOffIncomeSettlement", $"One-off income '{id}' is not settled.");

    public static Error Settled(Guid id) =>
        new("Conflict.OneOffIncomeSettled", $"One-off income '{id}' is settled — undo the settlement first.");

    public static Error CurrencyMismatch(string planned, string actual) =>
        new("Validation.CurrencyMismatch", $"The income is planned in {planned}, so it can't be settled on a {actual} account.");
}
