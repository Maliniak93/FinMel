using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features;

internal static class TransactionErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.Transaction", $"Transaction '{id}' was not found.");

    public static Error OversellsPosition(TransactionType type) =>
        new("Validation.OversellsPosition", $"This {type} would take the asset quantity below zero (selling more than the position).");

    public static Error MutationBreaksHistory(TransactionType type) =>
        new("Conflict.OversellsPosition", $"This change would make a later {type} take the asset quantity below zero (selling more than the position at some point in history).");

    public static Error TypeNotAllowedForClass(TransactionType type, AssetClass assetClass) =>
        new(
            "Validation.TransactionTypeNotAllowed",
            $"A {assetClass} asset accepts only {string.Join(", ", AssetTransactionTypes.Allowed(assetClass))} transactions, not {type}.");

    public static Error ConcurrentModification() =>
        new("Conflict.ConcurrentModification", "The asset was modified by another request in the meantime; reload and try again.");
}
