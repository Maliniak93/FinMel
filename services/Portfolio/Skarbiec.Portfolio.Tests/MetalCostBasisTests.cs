using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Metals;
using Skarbiec.Portfolio.Tests.Fixtures;
using static Skarbiec.Portfolio.Tests.Fixtures.MetalTradeFixtures;

namespace Skarbiec.Portfolio.Tests;

public sealed class MetalCostBasisTests
{
    [Fact]
    public void Compute_SellScalesCostByRemainingPieces()
    {
        var firstTransfer = Guid.NewGuid();
        var secondTransfer = Guid.NewGuid();
        Transaction[] trades =
        [
            Trade(TransactionType.Buy, 2m, firstTransfer, new DateOnly(2026, 10, 1)),
            Trade(TransactionType.Buy, 3m, secondTransfer, new DateOnly(2026, 10, 2)),
            Trade(TransactionType.Sell, 2m, Guid.NewGuid(), new DateOnly(2026, 10, 3))
        ];
        Transaction[] cashLegs =
        [
            CashLeg(TransactionType.Withdraw, firstTransfer, 2_000m, new DateOnly(2026, 10, 1)),
            CashLeg(TransactionType.Withdraw, secondTransfer, 3_300m, new DateOnly(2026, 10, 2))
        ];

        var basis = MetalCostBasis.Compute(trades, cashLegs);

        Assert.Multiple(
            () => Assert.Equal(3m, basis.Quantity),
            () => Assert.Equal(3_180m, basis.CostPln));
    }

    [Fact]
    public void Compute_ZeroPieces_CostIsZero()
    {
        var transfer = Guid.NewGuid();
        Transaction[] trades =
        [
            Trade(TransactionType.Buy, 2m, transfer, new DateOnly(2026, 10, 1)),
            Trade(TransactionType.Sell, 2m, Guid.NewGuid(), new DateOnly(2026, 10, 2))
        ];
        Transaction[] cashLegs = [CashLeg(TransactionType.Withdraw, transfer, 2_000m, new DateOnly(2026, 10, 1))];

        var basis = MetalCostBasis.Compute(trades, cashLegs);

        Assert.Multiple(
            () => Assert.Equal(0m, basis.Quantity),
            () => Assert.Equal(0m, basis.CostPln));
    }

    [Fact]
    public void Compute_BuyWithoutCashLeg_IsNull()
    {
        Transaction[] trades =
        [
            Trade(TransactionType.Buy, 2m, Guid.NewGuid(), new DateOnly(2026, 10, 1))
        ];

        var basis = MetalCostBasis.Compute(trades, []);

        Assert.Multiple(
            () => Assert.Equal(2m, basis.Quantity),
            () => Assert.Null(basis.CostPln));
    }
}
