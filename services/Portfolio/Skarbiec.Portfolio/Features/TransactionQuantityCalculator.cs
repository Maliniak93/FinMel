using System.Diagnostics;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features;

public static class TransactionQuantityCalculator
{
    public static Result<decimal> Recompute(IEnumerable<Transaction> transactions, Func<TransactionType, Error>? oversellError = null)
    {
        var history = History(transactions, oversellError);
        if (history.IsFailure)
        {
            return history.Error;
        }

        return history.Value.Count == 0 ? 0m : history.Value[^1].Quantity;
    }

    public static Result<IReadOnlyList<QuantityPoint>> History(IEnumerable<Transaction> transactions, Func<TransactionType, Error>? oversellError = null)
    {
        oversellError ??= TransactionErrors.OversellsPosition;
        var quantity = 0m;
        var points = new List<QuantityPoint>();

        // No intra-day ordering: a same-day tie replays inflows first, then by Id, so a top-up then a transfer out never fails on Guid order.
        var ordered = transactions
            .OrderBy(t => t.Date)
            .ThenBy(t => QuantityDelta(t) > 0 ? 0 : 1)
            .ThenBy(t => t.Id);

        foreach (var transaction in ordered)
        {
            quantity += QuantityDelta(transaction);

            if (quantity < 0)
            {
                return oversellError(transaction.Type);
            }

            if (points.Count > 0 && points[^1].Date == transaction.Date)
            {
                points[^1] = points[^1] with { Quantity = quantity };
            }
            else
            {
                points.Add(new QuantityPoint { Date = transaction.Date, Quantity = quantity });
            }
        }

        return points;
    }

    public static decimal QuantityDelta(Transaction transaction) => transaction.Type switch
    {
        TransactionType.Buy or TransactionType.Deposit => transaction.Quantity,
        TransactionType.Sell or TransactionType.Withdraw => -transaction.Quantity,
        TransactionType.Dividend or TransactionType.Interest => 0m,
        _ => throw new UnreachableException($"Unhandled transaction type '{transaction.Type}'.")
    };
}
