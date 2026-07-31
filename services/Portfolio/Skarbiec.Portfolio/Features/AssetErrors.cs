using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features;

internal static class AssetErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.Asset", $"Asset '{id}' was not found.");

    public static Error HasTransactions(Guid id) =>
        new("Conflict.AssetHasTransactions", $"Asset '{id}' has transactions and cannot be removed.");
}
