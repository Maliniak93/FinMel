using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Securities;

public sealed record SecurityCostBasisResult(decimal Quantity, decimal CostQuote, decimal? CostPln, decimal? AverageBuyPrice);

public static class SecurityCostBasis
{
    private const int AveragePriceDecimals = 4;

    // Same replay order as TransactionQuantityCalculator: by date, same-day inflows first, then by Id.
    public static SecurityCostBasisResult Compute(IEnumerable<Transaction> transactions)
    {
        var quantity = 0m;
        var costQuote = 0m;
        decimal? costPln = 0m;

        var ordered = transactions
            .OrderBy(t => t.Date)
            .ThenBy(t => TransactionQuantityCalculator.QuantityDelta(t) > 0 ? 0 : 1)
            .ThenBy(t => t.Id);

        foreach (var transaction in ordered)
        {
            switch (transaction.Type)
            {
                case TransactionType.Buy or TransactionType.Deposit:
                    var amount = transaction.Quantity * transaction.UnitPriceAmount;
                    quantity += transaction.Quantity;
                    costQuote += amount;
                    costPln = costPln is null || transaction.FxRateToPln is null ? null : costPln + amount * transaction.FxRateToPln;
                    break;

                case TransactionType.Sell or TransactionType.Withdraw:
                    var remaining = Math.Max(quantity - transaction.Quantity, 0m);
                    costQuote = quantity > 0 ? costQuote * remaining / quantity : 0m;
                    costPln = quantity > 0 ? costPln * remaining / quantity : 0m;
                    quantity = remaining;
                    break;
            }

            if (quantity == 0)
            {
                costQuote = 0m;
                costPln = 0m;
            }
        }

        var average = quantity > 0
            ? Math.Round(costQuote / quantity, AveragePriceDecimals, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        return new SecurityCostBasisResult(quantity, costQuote, costPln, average);
    }
}
