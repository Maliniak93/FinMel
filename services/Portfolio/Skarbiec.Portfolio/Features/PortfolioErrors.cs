using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features;

internal static class PortfolioErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.Portfolio", $"Portfolio '{id}' was not found.");

    public static Error DuplicateName(string name) =>
        new("Conflict.DuplicatePortfolioName", $"A portfolio named '{name}' already exists.");

    /// <summary>
    /// An archived portfolio is read-only: its assets and transactions can't be added, changed or
    /// removed until it is restored (archived-portfolio-out-of-net-worth). Raised only after the
    /// tenancy-scoped lookup succeeded, so a stranger still gets 404, never this 409.
    /// </summary>
    public static Error Archived(Guid id) =>
        new("Conflict.PortfolioArchived", $"Portfolio '{id}' is archived — restore it to make changes.");
}
