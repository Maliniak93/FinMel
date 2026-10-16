using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.RecurringExpenses;

internal static class RecurringExpenseErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.RecurringExpense", $"Recurring expense '{id}' was not found.");

    public static Error DuplicateName(string name) =>
        new("Conflict.DuplicateName", $"A recurring expense named '{name}' already exists.");
}
