using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.OneOffIncomes;

internal static class OneOffIncomeErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.OneOffIncome", $"One-off income '{id}' was not found.");
}
