using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits;

public sealed record DepositTerms
{
    public required decimal Principal { get; init; }
    public required DateOnly StartDate { get; init; }
    public required int TermLength { get; init; }
    public required DepositTermUnit TermUnit { get; init; }
    public required decimal AnnualInterestRatePercent { get; init; }
    public required DepositCapitalization Capitalization { get; init; }
    public required bool TaxExempt { get; init; }
}

public sealed record DepositInterestPeriod
{
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public required int Days { get; init; }
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }
}

public sealed record DepositProjection
{
    public required DateOnly MaturityDate { get; init; }
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }
    public required decimal FinalAmount { get; init; }

    public required decimal NetProfitPercent { get; init; }

    public required IReadOnlyList<DepositInterestPeriod> Periods { get; init; }
}

public static class DepositInterestMath
{
    public static DateOnly MaturityDate(DateOnly startDate, int termLength, DepositTermUnit termUnit) => termUnit switch
    {
        DepositTermUnit.Days => startDate.AddDays(termLength),
        DepositTermUnit.Months => startDate.AddMonths(termLength),
        _ => throw new ArgumentOutOfRangeException(nameof(termUnit), termUnit, "Unmapped DepositTermUnit.")
    };

    public static DepositProjection Project(DepositTerms terms)
    {
        var maturityDate = MaturityDate(terms.StartDate, terms.TermLength, terms.TermUnit);
        int? stepMonths = terms.Capitalization switch
        {
            DepositCapitalization.AtMaturity => null,
            DepositCapitalization.Monthly => 1,
            DepositCapitalization.Quarterly => 3,
            DepositCapitalization.Yearly => 12,
            _ => throw new ArgumentOutOfRangeException(nameof(terms), terms.Capitalization, "Unmapped DepositCapitalization.")
        };

        var periods = new List<DepositInterestPeriod>();
        var balance = terms.Principal;
        var periodStart = terms.StartDate;

        for (var k = 1; periodStart < maturityDate; k++)
        {
            // Period ends step from the start date, not the previous end, so a month-end start keeps landing on month ends.
            var periodEnd = stepMonths is { } step
                ? Min(terms.StartDate.AddMonths(k * step), maturityDate)
                : maturityDate;
            var days = periodEnd.DayNumber - periodStart.DayNumber;

            var gross = Math.Round(
                balance * terms.AnnualInterestRatePercent / 100m * days / 365m, 2, MidpointRounding.AwayFromZero);
            var tax = BelkaTax.On(gross, terms.TaxExempt);
            var net = gross - tax;
            balance += net;

            periods.Add(new DepositInterestPeriod
            {
                StartDate = periodStart,
                EndDate = periodEnd,
                Days = days,
                GrossInterest = gross,
                Tax = tax,
                NetInterest = net
            });

            periodStart = periodEnd;
        }

        var netInterest = periods.Sum(p => p.NetInterest);

        return new DepositProjection
        {
            MaturityDate = maturityDate,
            GrossInterest = periods.Sum(p => p.GrossInterest),
            Tax = periods.Sum(p => p.Tax),
            NetInterest = netInterest,
            FinalAmount = terms.Principal + netInterest,
            NetProfitPercent = Math.Round(netInterest / terms.Principal * 100m, 4, MidpointRounding.AwayFromZero),
            Periods = periods
        };
    }

    private static DateOnly Min(DateOnly first, DateOnly second) => first < second ? first : second;
}
