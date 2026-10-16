using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.RecurringExpenses;

internal static class RecurringExpenseErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.RecurringExpense", $"Recurring expense '{id}' was not found.");

    public static Error DuplicateName(string name) =>
        new("Conflict.DuplicateName", $"A recurring expense named '{name}' already exists.");

    public static Error ChangeNotFound(Guid id, string month) =>
        new("NotFound.RecurringExpenseChange", $"Recurring expense '{id}' has no change starting in {month}.");

    public static Error FirstVersion(string month) =>
        new("Conflict.FirstVersion", $"The change in {month} is the expense's first version — end or delete the expense instead.");
}
