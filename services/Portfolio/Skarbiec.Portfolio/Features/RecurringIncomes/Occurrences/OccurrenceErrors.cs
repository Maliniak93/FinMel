using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;

internal static class OccurrenceErrors
{
    public static Error NotFound(Guid id, string month) =>
        new("NotFound.RecurringIncomeOccurrence", $"Recurring income '{id}' has no occurrence for {month}.");

    public static Error Settled(string month) =>
        new("Conflict.OccurrenceSettled", $"The occurrence for {month} is settled — undo it first.");

    public static Error CurrencyMismatch(string planned, string actual) =>
        new("Validation.CurrencyMismatch", $"The occurrence is planned in {planned}, so it can't be settled on a {actual} account.");

    public static Error FutureDate(DateOnly today) =>
        new("Validation.ActualDateInFuture", $"The actual date can't be after today ({today:yyyy-MM-dd}).");
}
