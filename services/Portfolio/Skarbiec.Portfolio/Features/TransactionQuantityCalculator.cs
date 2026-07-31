using System.Diagnostics;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features;

/// <summary>
/// The single code path deriving <see cref="Asset.Quantity"/> from a transaction history
/// ("light" event sourcing, ADR-009) — shared by RecordTransaction (T1.3) and, later,
/// UpdateTransaction/DeleteTransaction (T1.4). Replays the given transactions in chronological
/// order and fails if a Sell/Withdraw would take the running quantity below zero at any point in
/// history, not just at the end.
/// </summary>
public static class TransactionQuantityCalculator
{
    public static Result<decimal> Recompute(IEnumerable<Transaction> transactions)
    {
        var quantity = 0m;

        // Same-day ties break on Id — arbitrary but deterministic; the domain doesn't track
        // intra-day ordering, so this is a reasonable simplification (not exercised by any AC).
        foreach (var transaction in transactions.OrderBy(t => t.Date).ThenBy(t => t.Id))
        {
            quantity += QuantityDelta(transaction);

            if (quantity < 0)
            {
                return TransactionErrors.OversellsPosition(transaction.Type);
            }
        }

        return quantity;
    }

    /// <summary>Buy/Deposit increase quantity, Sell/Withdraw decrease it; Dividend/Interest/Fee are value-only and don't affect quantity.</summary>
    private static decimal QuantityDelta(Transaction transaction) => transaction.Type switch
    {
        TransactionType.Buy or TransactionType.Deposit => transaction.Quantity,
        TransactionType.Sell or TransactionType.Withdraw => -transaction.Quantity,
        TransactionType.Dividend or TransactionType.Interest or TransactionType.Fee => 0m,
        _ => throw new UnreachableException($"Unhandled transaction type '{transaction.Type}'.")
    };
}
