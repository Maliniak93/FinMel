using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features;

internal static class PortfolioErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.Portfolio", $"Portfolio '{id}' was not found.");

    public static Error DuplicateName(string name) =>
        new("Conflict.DuplicatePortfolioName", $"A portfolio named '{name}' already exists.");

    public static Error Archived(Guid id) =>
        new("Conflict.PortfolioArchived", $"Portfolio '{id}' is archived — restore it to make changes.");

    public static Error AssetArchived(Guid id) =>
        new("Conflict.AssetArchived", $"Asset '{id}' is archived — restore it to make changes.");
}
