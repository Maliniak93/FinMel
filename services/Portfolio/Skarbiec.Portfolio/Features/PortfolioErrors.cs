using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features;

internal static class PortfolioErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.Portfolio", $"Portfolio '{id}' was not found.");

    public static Error DuplicateName(string name) =>
        new("Conflict.DuplicatePortfolioName", $"A portfolio named '{name}' already exists.");
}
