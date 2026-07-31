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

    public static Error ConcurrentModification() =>
        new("Conflict.ConcurrentModification", "The asset was modified by another request in the meantime; reload and try again.");
}
