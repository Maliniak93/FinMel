using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Securities;

namespace Skarbiec.Portfolio.Tests;

public sealed class SecurityCostBasisTests
{
    [Fact]
    public void WeightedAverage_SellKeepsAverage()
    {
        Transaction[] transactions =
        [
            NewTransaction(TransactionType.Buy, 10m, 100m, 4.00m, new DateOnly(2026, 1, 1)),
            NewTransaction(TransactionType.Buy, 10m, 120m, 4.20m, new DateOnly(2026, 1, 2)),
            NewTransaction(TransactionType.Sell, 5m, 130m, 4.25m, new DateOnly(2026, 1, 3))
        ];

        var basis = SecurityCostBasis.Compute(transactions);

        Assert.Multiple(
            () => Assert.Equal(15m, basis.Quantity),
            () => Assert.Equal(110.0000m, basis.AverageBuyPrice),
            () => Assert.Equal(1650m, basis.CostQuote),
            () => Assert.Equal(6780m, basis.CostPln));
    }

    [Fact]
    public void MissingFx_NullsPlnCost()
    {
        Transaction[] transactions =
        [
            NewTransaction(TransactionType.Buy, 10m, 100m, 4.00m, new DateOnly(2026, 1, 1)),
            NewTransaction(TransactionType.Buy, 10m, 120m, 4.20m, new DateOnly(2026, 1, 2)),
            NewTransaction(TransactionType.Sell, 5m, 130m, 4.25m, new DateOnly(2026, 1, 3)),
            NewTransaction(TransactionType.Buy, 5m, 100m, null, new DateOnly(2026, 1, 4))
        ];

        var basis = SecurityCostBasis.Compute(transactions);

        Assert.Multiple(
            () => Assert.Equal(20m, basis.Quantity),
            () => Assert.Equal(2150m, basis.CostQuote),
            () => Assert.Null(basis.CostPln));
    }

    private static Transaction NewTransaction(TransactionType type, decimal quantity, decimal unitPrice, decimal? fxRate, DateOnly date) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = Guid.NewGuid(),
        Type = type,
        Quantity = quantity,
        UnitPriceAmount = unitPrice,
        FxRateToPln = fxRate,
        Date = date
    };
}
