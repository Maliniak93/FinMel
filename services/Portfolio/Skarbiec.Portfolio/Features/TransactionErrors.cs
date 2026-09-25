using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features;

internal static class TransactionErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.Transaction", $"Transaction '{id}' was not found.");

    public static Error OversellsPosition(TransactionType type) =>
        new("Validation.OversellsPosition", $"This {type} would take the asset quantity below zero (selling more than the position).");

    /// <summary>
    /// Same invariant as <see cref="OversellsPosition"/> but raised by UpdateTransaction/DeleteTransaction
    /// (T1.4) instead of RecordTransaction — an edit/delete that breaks a later transaction in the
    /// existing history is a conflict with what's already recorded (409), not a validation failure
    /// on new input (400).
    /// </summary>
    public static Error MutationBreaksHistory(TransactionType type) =>
        new("Conflict.OversellsPosition", $"This change would make a later {type} take the asset quantity below zero (selling more than the position at some point in history).");

    /// <summary>
    /// A transaction type the asset's class does not accept (<see cref="AssetTransactionTypes"/>) — a
    /// 400 on the new input, like <see cref="OversellsPosition"/>.
    /// </summary>
    public static Error TypeNotAllowedForClass(TransactionType type, AssetClass assetClass) =>
        new(
            "Validation.TransactionTypeNotAllowed",
            $"A {assetClass} asset accepts only {string.Join(", ", AssetTransactionTypes.Allowed(assetClass))} transactions, not {type}.");

    public static Error ConcurrentModification() =>
        new("Conflict.ConcurrentModification", "The asset was modified by another request in the meantime; reload and try again.");
}
