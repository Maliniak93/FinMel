using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features;

internal static class TransactionErrors
{
    public static Error OversellsPosition(TransactionType type) =>
        new("Validation.OversellsPosition", $"This {type} would take the asset quantity below zero (selling more than the position).");
}
