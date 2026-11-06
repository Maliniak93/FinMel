using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Metals;

public sealed record MetalCostBasisResult(decimal Quantity, decimal? CostPln);

public static class MetalCostBasis
{
    // Same replay order as SecurityCostBasis: by date, same-day inflows first, then by Id.
    public static MetalCostBasisResult Compute(IReadOnlyList<Transaction> trades, IReadOnlyList<Transaction> cashLegs)
    {
        var payments = cashLegs
            .Where(l => l.Type == TransactionType.Withdraw && l.TransferId is not null)
            .GroupBy(l => l.TransferId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var quantity = 0m;
        decimal? cost = 0m;

        var ordered = trades
            .OrderBy(t => t.Date)
            .ThenBy(t => TransactionQuantityCalculator.QuantityDelta(t) > 0 ? 0 : 1)
            .ThenBy(t => t.Id);

        foreach (var trade in ordered)
        {
            switch (trade.Type)
            {
                case TransactionType.Buy:
                    quantity += trade.Quantity;
                    cost = cost is null || trade.TransferId is not { } transferId || !payments.TryGetValue(transferId, out var paid)
                        ? null
                        : cost + paid;
                    break;

                case TransactionType.Sell:
                    var remaining = Math.Max(quantity - trade.Quantity, 0m);
                    cost = quantity > 0 ? cost * remaining / quantity : 0m;
                    quantity = remaining;
                    break;
            }

            if (quantity == 0)
            {
                cost = 0m;
            }
        }

        return new MetalCostBasisResult(quantity, cost);
    }
}
