using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Bonds;

namespace Skarbiec.Portfolio.Tests;

public sealed class BondValuationTests
{
    [Fact]
    public void Edo_MidYear2()
    {
        var bond = NewBond(TreasuryBondType.Edo, new DateOnly(2024, 3, 1), bondCount: 10, firstPeriodRate: 5.35m, fee: 3.00m);
        var settlements = new[] { NewSettlement(bond, 1, 5.35m, 53.50m) };
        var today = new DateOnly(2025, 5, 13);
        var catalog = new Dictionary<int, decimal> { [2] = 4.00m };

        var outcome = BondValuation.Today(bond, settlements, catalog, today);

        Assert.Null(outcome.Reason);
        var estimate = Assert.IsType<BondEstimateResponse>(outcome.Estimate);
        var expectedNet = BondRedemptionMath.Early(
            TreasuryBondType.Edo, [5.35m, 4.00m], new DateOnly(2025, 3, 1), new DateOnly(2026, 3, 1), today, 3.00m, 100m, 10, false).Proceeds;
        Assert.Equal(10 * Math.Round(105.35m * (1m + 0.04m * 73m / 365m), 2, MidpointRounding.AwayFromZero), estimate.GrossValue);
        Assert.Equal(1_061.90m, estimate.GrossValue);
        Assert.Equal(expectedNet, estimate.NetValue);
        Assert.Equal(today, estimate.AsOf);
    }

    [Fact]
    public void Ror_UnsettledCoupon()
    {
        var bond = NewBond(TreasuryBondType.Ror, new DateOnly(2026, 7, 10), bondCount: 10, firstPeriodRate: 6.00m, fee: 0.50m);
        var settlements = new[] { NewSettlement(bond, 1, 6.00m, 5.00m) };
        var today = new DateOnly(2026, 9, 25);
        var catalog = new Dictionary<int, decimal> { [2] = 5.00m, [3] = 4.50m };

        var outcome = BondValuation.Today(bond, settlements, catalog, today);

        Assert.Null(outcome.Reason);
        var estimate = Assert.IsType<BondEstimateResponse>(outcome.Estimate);
        var unsettledGross = 10 * 0.42m;
        var early = BondRedemptionMath.Early(
            TreasuryBondType.Ror, [6.00m, 5.00m, 4.50m], new DateOnly(2026, 9, 10), new DateOnly(2026, 10, 10), today, 0.50m, 100m, 10, false);
        Assert.Equal(10 * (100m + 0.42m + 0.19m), estimate.GrossValue);
        Assert.Equal(early.Proceeds + unsettledGross - BelkaTax.On(unsettledGross, false), estimate.NetValue);
    }

    [Fact]
    public void MissingRate_IsNull()
    {
        var bond = NewBond(TreasuryBondType.Edo, new DateOnly(2024, 3, 1), bondCount: 10, firstPeriodRate: 5.35m, fee: 3.00m);
        var settlements = new[] { NewSettlement(bond, 1, 5.35m, 53.50m) };

        var outcome = BondValuation.Today(bond, settlements, new Dictionary<int, decimal>(), new DateOnly(2025, 5, 13));

        Assert.Null(outcome.Estimate);
        Assert.Equal(BondEstimateUnavailableReason.RateMissing, outcome.Reason);
    }

    internal static TreasuryBond NewBond(
        TreasuryBondType type, DateOnly purchaseDate, int bondCount, decimal firstPeriodRate, decimal fee, decimal price = 100m, bool taxExempt = false) => new()
        {
            AssetId = Guid.NewGuid(),
            SeriesCode = "TEST0000",
            Type = type,
            PurchaseDate = purchaseDate,
            BondCount = bondCount,
            PurchasePricePerBond = price,
            FirstPeriodRatePercent = firstPeriodRate,
            EarlyRedemptionFeePerBond = fee,
            TaxExempt = taxExempt,
            MaturityDate = BondSchedule.MaturityDate(type, purchaseDate)
        };

    internal static BondInterestSettlement NewSettlement(TreasuryBond bond, int periodIndex, decimal ratePercent, decimal grossInterest)
    {
        var period = BondSchedule.Periods(bond.Type, bond.PurchaseDate)[periodIndex - 1];

        return new BondInterestSettlement
        {
            Id = Guid.NewGuid(),
            AssetId = bond.AssetId,
            PeriodIndex = periodIndex,
            PeriodStart = period.Start,
            PeriodEnd = period.End,
            RatePercent = ratePercent,
            BondCount = bond.BondCount,
            GrossInterest = grossInterest,
            Tax = 0m
        };
    }
}
