using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// term-deposits AC-3: invariants of <see cref="DepositInterestMath.Project"/> over a fixed-seed
/// spread of valid terms (every capitalisation, both term units, taxed and exempt, rates from 0 to
/// 100 %) — the arithmetic identities hold for every one, not just the worked examples in
/// <see cref="DepositInterestMathTests"/>.
/// </summary>
public sealed class DepositInterestMathPropertyTests
{
    private const int CaseCount = 300;

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 0; seed < CaseCount; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Project_AnyValidTerms_NetPlusTaxEqualsGrossAndFinalIsPrincipalPlusNet(int seed)
    {
        var terms = RandomTerms(seed);

        var projection = DepositInterestMath.Project(terms);

        Assert.NotEmpty(projection.Periods);
        Assert.Equal(projection.GrossInterest, projection.NetInterest + projection.Tax);
        Assert.Equal(terms.Principal + projection.NetInterest, projection.FinalAmount);
        Assert.Equal(projection.GrossInterest, projection.Periods.Sum(p => p.GrossInterest));
        Assert.Equal(projection.Tax, projection.Periods.Sum(p => p.Tax));
        Assert.Equal(projection.NetInterest, projection.Periods.Sum(p => p.NetInterest));
        Assert.True(projection.GrossInterest >= 0m);
        Assert.True(projection.Tax >= 0m);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Project_AnyValidTerms_EveryPeriodTaxIsBelkaRoundedUpToGrosz(int seed)
    {
        var terms = RandomTerms(seed);

        var projection = DepositInterestMath.Project(terms);

        Assert.NotEmpty(projection.Periods);
        foreach (var period in projection.Periods)
        {
            var expectedTax = terms.TaxExempt ? 0m : Math.Ceiling(period.GrossInterest * 0.19m * 100m) / 100m;
            Assert.Equal(expectedTax, period.Tax);
            Assert.Equal(period.GrossInterest - period.Tax, period.NetInterest);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Project_AnyValidTerms_EveryPeriodGrossIsActual365OnCompoundedBalance(int seed)
    {
        var terms = RandomTerms(seed);

        var projection = DepositInterestMath.Project(terms);

        Assert.NotEmpty(projection.Periods);
        var balance = terms.Principal;
        foreach (var period in projection.Periods)
        {
            var expectedGross = Math.Round(
                balance * terms.AnnualInterestRatePercent / 100m * period.Days / 365m, 2, MidpointRounding.AwayFromZero);
            Assert.Equal(expectedGross, period.GrossInterest);
            balance += period.NetInterest;
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Project_AnyValidTerms_PeriodsTileTheTermFromStartToMaturity(int seed)
    {
        var terms = RandomTerms(seed);

        var projection = DepositInterestMath.Project(terms);

        Assert.Equal(DepositInterestMath.MaturityDate(terms.StartDate, terms.TermLength, terms.TermUnit), projection.MaturityDate);
        Assert.NotEmpty(projection.Periods);
        Assert.Equal(terms.StartDate, projection.Periods[0].StartDate);
        Assert.Equal(projection.MaturityDate, projection.Periods[^1].EndDate);
        for (var i = 0; i < projection.Periods.Count; i++)
        {
            var period = projection.Periods[i];
            Assert.True(period.Days > 0, $"Period {i} is empty.");
            Assert.Equal(period.EndDate.DayNumber - period.StartDate.DayNumber, period.Days);
            if (i > 0)
            {
                Assert.Equal(projection.Periods[i - 1].EndDate, period.StartDate);
            }
        }

        if (terms.Capitalization == DepositCapitalization.AtMaturity)
        {
            Assert.Single(projection.Periods);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Project_TaxExempt_TaxIsZeroAndNetEqualsGross(int seed)
    {
        var terms = RandomTerms(seed) with { TaxExempt = true };

        var projection = DepositInterestMath.Project(terms);

        Assert.NotEmpty(projection.Periods);
        Assert.Equal(0m, projection.Tax);
        Assert.All(projection.Periods, p => Assert.Equal(0m, p.Tax));
        Assert.Equal(projection.GrossInterest, projection.NetInterest);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Project_ZeroRate_EarnsNothing(int seed)
    {
        var terms = RandomTerms(seed) with { AnnualInterestRatePercent = 0m };

        var projection = DepositInterestMath.Project(terms);

        Assert.NotEmpty(projection.Periods);
        Assert.Equal(DepositInterestMath.MaturityDate(terms.StartDate, terms.TermLength, terms.TermUnit), projection.MaturityDate);
        Assert.Equal(0m, projection.GrossInterest);
        Assert.Equal(0m, projection.Tax);
        Assert.Equal(0m, projection.NetInterest);
        Assert.Equal(terms.Principal, projection.FinalAmount);
        Assert.Equal(0m, projection.NetProfitPercent);
    }

    /// <summary>NetProfitPercent is the net interest as a percentage of the principal.</summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Project_AnyValidTerms_NetProfitPercentIsNetOverPrincipal(int seed)
    {
        var terms = RandomTerms(seed);

        var projection = DepositInterestMath.Project(terms);

        Assert.NotEmpty(projection.Periods);
        var expected = projection.NetInterest / terms.Principal * 100m;
        Assert.InRange(projection.NetProfitPercent, expected - 0.01m, expected + 0.01m);
    }

    private static DepositTerms RandomTerms(int seed)
    {
        var random = new Random(seed);
        var termUnit = random.Next(2) == 0 ? DepositTermUnit.Days : DepositTermUnit.Months;
        var capitalizations = Enum.GetValues<DepositCapitalization>();

        return new DepositTerms
        {
            // 0.01 .. 5 000 000.00, always to grosze.
            Principal = random.Next(1, 500_000_001) / 100m,
            StartDate = new DateOnly(2020, 1, 1).AddDays(random.Next(0, 3650)),
            TermLength = termUnit == DepositTermUnit.Days ? random.Next(1, 3651) : random.Next(1, 121),
            TermUnit = termUnit,
            // 0.0000 .. 100.0000 %, numeric(7,4).
            AnnualInterestRatePercent = random.Next(0, 1_000_001) / 10_000m,
            Capitalization = capitalizations[random.Next(capitalizations.Length)],
            TaxExempt = random.Next(4) == 0
        };
    }
}
