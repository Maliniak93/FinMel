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

    /// <summary>
    /// An archived asset is read-only the same way (asset-archive): no edit, transaction or deposit
    /// write until it is restored — removal stays allowed. Checked after <see cref="Archived"/>, so an
    /// archived asset inside an archived portfolio answers with the portfolio's conflict.
    /// </summary>
    public static Error AssetArchived(Guid id) =>
        new("Conflict.AssetArchived", $"Asset '{id}' is archived — restore it to make changes.");
}
