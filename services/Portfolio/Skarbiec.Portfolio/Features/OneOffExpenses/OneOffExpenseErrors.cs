using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.OneOffExpenses;

internal static class OneOffExpenseErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.OneOffExpense", $"One-off expense '{id}' was not found.");
}
