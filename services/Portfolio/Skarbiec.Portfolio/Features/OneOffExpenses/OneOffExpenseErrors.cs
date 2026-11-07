using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.OneOffExpenses;

internal static class OneOffExpenseErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.OneOffExpense", $"One-off expense '{id}' was not found.");

    public static Error NotSettled(Guid id) =>
        new("NotFound.OneOffExpenseSettlement", $"One-off expense '{id}' is not settled.");

    public static Error Settled(Guid id) =>
        new("Conflict.OneOffExpenseSettled", $"One-off expense '{id}' is settled — undo the settlement first.");
}
