using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// term-deposits: the worked examples of <see cref="DepositInterestMath"/> — pure, no host, no DB.
/// Gross interest per period is actual/365 rounded half-away-from-zero to grosze; the Belka tax is
/// 19 % of each period's gross, rounded up to grosze; the net is compounded into the next period.
/// </summary>
public sealed class DepositInterestMathTests
{
    /// <summary>AC-1.</summary>
    [Fact]
    public void AtMaturity_ThreeMonths_AppliesBelkaRoundedUpToGrosz()
    {
        var projection = DepositInterestMath.Project(new DepositTerms
        {
            Principal = 10_000.00m,
            StartDate = new DateOnly(2026, 1, 15),
            TermLength = 3,
            TermUnit = DepositTermUnit.Months,
            AnnualInterestRatePercent = 6m,
            Capitalization = DepositCapitalization.AtMaturity,
            TaxExempt = false
        });

        Assert.Equal(new DateOnly(2026, 4, 15), projection.MaturityDate);
        var period = Assert.Single(projection.Periods);
        Assert.Equal(90, period.Days);
        Assert.Equal(147.95m, projection.GrossInterest);
        Assert.Equal(28.12m, projection.Tax);
        Assert.Equal(119.83m, projection.NetInterest);
        Assert.Equal(10_119.83m, projection.FinalAmount);
    }

    /// <summary>AC-2: each monthly period is taxed on its own and its net compounds into the next.</summary>
    [Fact]
    public void Monthly_ThreeMonths_TaxesAndCompoundsEachPeriod()
    {
        var projection = DepositInterestMath.Project(new DepositTerms
        {
            Principal = 12_000.00m,
            StartDate = new DateOnly(2026, 1, 1),
            TermLength = 3,
            TermUnit = DepositTermUnit.Months,
            AnnualInterestRatePercent = 6m,
            Capitalization = DepositCapitalization.Monthly,
            TaxExempt = false
        });

        Assert.Equal(new DateOnly(2026, 4, 1), projection.MaturityDate);
        Assert.Equal([31, 28, 31], projection.Periods.Select(p => p.Days));
        Assert.Equal([61.15m, 55.46m, 61.63m], projection.Periods.Select(p => p.GrossInterest));
        Assert.Equal([11.62m, 10.54m, 11.71m], projection.Periods.Select(p => p.Tax));
        Assert.Equal([49.53m, 44.92m, 49.92m], projection.Periods.Select(p => p.NetInterest));
        Assert.Equal(178.24m, projection.GrossInterest);
        Assert.Equal(33.87m, projection.Tax);
        Assert.Equal(144.37m, projection.NetInterest);
        Assert.Equal(12_144.37m, projection.FinalAmount);
    }

    /// <summary>AC-4: months clamp to the month's end; days are plain calendar days.</summary>
    [Theory]
    [InlineData(1, DepositTermUnit.Months, 2026, 2, 28)]
    [InlineData(45, DepositTermUnit.Days, 2026, 3, 17)]
    public void MaturityDate_ClampsMonthEndAndCountsDays(
        int termLength, DepositTermUnit termUnit, int year, int month, int day)
    {
        var maturity = DepositInterestMath.MaturityDate(new DateOnly(2026, 1, 31), termLength, termUnit);

        Assert.Equal(new DateOnly(year, month, day), maturity);
    }

    /// <summary>
    /// Capitalisation periods run from the start date (each end is <c>StartDate.AddMonths(k × step)</c>)
    /// and the last one is cut at maturity: 45 days from 2026-01-15, capitalised monthly, is one full
    /// month (31 days) and a 14-day stub.
    /// </summary>
    [Fact]
    public void Monthly_DayTermNotAWholeNumberOfMonths_CutsLastPeriodAtMaturity()
    {
        var projection = DepositInterestMath.Project(new DepositTerms
        {
            Principal = 5_000m,
            StartDate = new DateOnly(2026, 1, 15),
            TermLength = 45,
            TermUnit = DepositTermUnit.Days,
            AnnualInterestRatePercent = 5m,
            Capitalization = DepositCapitalization.Monthly,
            TaxExempt = true
        });

        Assert.Equal(new DateOnly(2026, 3, 1), projection.MaturityDate);
        Assert.Collection(
            projection.Periods,
            first =>
            {
                Assert.Equal(new DateOnly(2026, 1, 15), first.StartDate);
                Assert.Equal(new DateOnly(2026, 2, 15), first.EndDate);
                Assert.Equal(31, first.Days);
            },
            last =>
            {
                Assert.Equal(new DateOnly(2026, 2, 15), last.StartDate);
                Assert.Equal(new DateOnly(2026, 3, 1), last.EndDate);
                Assert.Equal(14, last.Days);
            });
    }

    /// <summary>Quarterly and yearly periods step by 3 and 12 months from the start date.</summary>
    [Theory]
    [InlineData(DepositCapitalization.Quarterly, 12, 4)]
    [InlineData(DepositCapitalization.Yearly, 24, 2)]
    [InlineData(DepositCapitalization.Monthly, 12, 12)]
    [InlineData(DepositCapitalization.AtMaturity, 24, 1)]
    public void Capitalization_WholeYears_ProducesOnePeriodPerStep(
        DepositCapitalization capitalization, int termMonths, int expectedPeriods)
    {
        var start = new DateOnly(2026, 3, 31);
        var projection = DepositInterestMath.Project(new DepositTerms
        {
            Principal = 1_000m,
            StartDate = start,
            TermLength = termMonths,
            TermUnit = DepositTermUnit.Months,
            AnnualInterestRatePercent = 4m,
            Capitalization = capitalization,
            TaxExempt = false
        });

        Assert.Equal(expectedPeriods, projection.Periods.Count);
        var step = capitalization switch
        {
            DepositCapitalization.Monthly => 1,
            DepositCapitalization.Quarterly => 3,
            DepositCapitalization.Yearly => 12,
            _ => termMonths
        };
        for (var k = 0; k < expectedPeriods; k++)
        {
            Assert.Equal(start.AddMonths(k * step), projection.Periods[k].StartDate);
            Assert.Equal(start.AddMonths((k + 1) * step), projection.Periods[k].EndDate);
        }
    }

    /// <summary>An IKE/IKZE deposit pays no Belka tax: the AC-1 deposit keeps its whole 147.95 gross.</summary>
    [Fact]
    public void TaxExempt_AtMaturity_NetEqualsGross()
    {
        var projection = DepositInterestMath.Project(new DepositTerms
        {
            Principal = 10_000.00m,
            StartDate = new DateOnly(2026, 1, 15),
            TermLength = 3,
            TermUnit = DepositTermUnit.Months,
            AnnualInterestRatePercent = 6m,
            Capitalization = DepositCapitalization.AtMaturity,
            TaxExempt = true
        });

        Assert.Equal(147.95m, projection.GrossInterest);
        Assert.Equal(0m, projection.Tax);
        Assert.Equal(147.95m, projection.NetInterest);
        Assert.Equal(10_147.95m, projection.FinalAmount);
    }
}
