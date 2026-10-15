using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.RecurringIncomes;

internal static class RecurringIncomeErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.RecurringIncome", $"Recurring income '{id}' was not found.");

    public static Error DuplicateName(string name) =>
        new("Conflict.DuplicateName", $"A recurring income named '{name}' already exists.");

    public static Error ChangeNotFound(Guid id, string month) =>
        new("NotFound.RecurringIncomeChange", $"Recurring income '{id}' has no change starting in {month}.");

    public static Error FirstVersion(string month) =>
        new("Conflict.FirstVersion", $"The change in {month} is the income's first version — end or delete the income instead.");

    public static Error EndBeforeStart(DateOnly start) =>
        new("Validation.EndBeforeStart", $"The end date can't be before the start date {start:yyyy-MM-dd}.");
}
