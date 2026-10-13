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

    [Fact]
    public void BuyFee_InCost()
    {
        Transaction[] plnBuy = [NewTransaction(TransactionType.Buy, 10m, 120m, 1m, new DateOnly(2026, 10, 12), fee: 3m)];
        Transaction[] usdBuy = [NewTransaction(TransactionType.Buy, 2m, 100m, 4.00m, new DateOnly(2026, 10, 12), fee: 5m)];
        Transaction[] plnBuyThenSell =
        [
            plnBuy[0],
            NewTransaction(TransactionType.Sell, 4m, 130m, 1m, new DateOnly(2026, 10, 13), fee: 3m)
        ];

        var pln = SecurityCostBasis.Compute(plnBuy);
        var usd = SecurityCostBasis.Compute(usdBuy);
        var afterSell = SecurityCostBasis.Compute(plnBuyThenSell);

        Assert.Multiple(
            () => Assert.Equal(1_203m, pln.CostQuote),
            () => Assert.Equal(1_203m, pln.CostPln),
            () => Assert.Equal(120.3m, pln.AverageBuyPrice),
            () => Assert.Equal(201.25m, usd.CostQuote),
            () => Assert.Equal(805m, usd.CostPln),
            () => Assert.Equal(721.8m, afterSell.CostQuote),
            () => Assert.Equal(721.8m, afterSell.CostPln));
    }

    [Fact]
    public void Buy_WithAmountPln_AddsItToCostPln()
    {
        Transaction[] buys =
        [
            NewTransaction(TransactionType.Buy, 10m, 100m, 4.3125m, new DateOnly(2026, 1, 1), fee: 5m, amountPln: 4_312.50m),
            NewTransaction(TransactionType.Buy, 10m, 120m, 4.00m, new DateOnly(2026, 1, 2))
        ];
        Transaction[] buysThenSell = [.. buys, NewTransaction(TransactionType.Sell, 5m, 130m, 4.25m, new DateOnly(2026, 1, 3))];

        var bought = SecurityCostBasis.Compute(buys);
        var afterSell = SecurityCostBasis.Compute(buysThenSell);

        Assert.Multiple(
            () => Assert.Equal(9_117.50m, bought.CostPln),
            () => Assert.Equal(6_838.125m, afterSell.CostPln));
    }

    [Fact]
    public void AveragePricePln_IsCostPlnPerUnit()
    {
        Transaction[] transactions =
        [
            NewTransaction(TransactionType.Buy, 10m, 100m, 4.3125m, new DateOnly(2026, 1, 1), fee: 5m, amountPln: 4_312.50m),
            NewTransaction(TransactionType.Buy, 10m, 120m, 4.00m, new DateOnly(2026, 1, 2)),
            NewTransaction(TransactionType.Sell, 5m, 130m, 4.25m, new DateOnly(2026, 1, 3))
        ];

        var basis = SecurityCostBasis.Compute(transactions);

        Assert.Equal(455.8750m, basis.AveragePricePln);
    }

    [Fact]
    public void AveragePricePln_NullWhenCostPlnNull()
    {
        Transaction[] transactions = [NewTransaction(TransactionType.Buy, 5m, 100m, null, new DateOnly(2026, 1, 1))];

        var basis = SecurityCostBasis.Compute(transactions);

        Assert.Null(basis.CostPln);
        Assert.Null(basis.AveragePricePln);
    }

    private static Transaction NewTransaction(
        TransactionType type,
        decimal quantity,
        decimal unitPrice,
        decimal? fxRate,
        DateOnly date,
        decimal? fee = null,
        decimal? amountPln = null) => new()
        {
            AmountPln = amountPln,
            FeeAmount = fee,
            Id = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            Type = type,
            Quantity = quantity,
            UnitPriceAmount = unitPrice,
            FxRateToPln = fxRate,
            Date = date
        };
}
