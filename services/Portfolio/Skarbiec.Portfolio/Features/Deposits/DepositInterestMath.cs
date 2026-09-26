using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits;

/// <summary>The inputs of <see cref="DepositInterestMath.Project"/> — a term deposit's terms, free of any entity or request type.</summary>
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

/// <summary>One capitalisation period: interest earned on the balance at its start, taxed on its own.</summary>
public sealed record DepositInterestPeriod
{
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public required int Days { get; init; }
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }
}

/// <summary>What a term deposit pays out at maturity, with the per-period breakdown it is summed from.</summary>
public sealed record DepositProjection
{
    public required DateOnly MaturityDate { get; init; }
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }
    public required decimal FinalAmount { get; init; }

    /// <summary><see cref="NetInterest"/> as a percentage of the principal, to 4 decimal places.</summary>
    public required decimal NetProfitPercent { get; init; }

    public required IReadOnlyList<DepositInterestPeriod> Periods { get; init; }
}

/// <summary>
/// Pure term-deposit arithmetic (term-deposits) — no I/O, no clock. Day count is actual/365 and the
/// rate is fixed for the whole term. Each capitalisation period's gross interest is rounded
/// half-away-from-zero to grosze (bank-style); its Belka tax is 19 % of that gross, rounded up to
/// grosze (art. 63 § 1a OP); the net compounds into the next period's balance.
/// </summary>
public static class DepositInterestMath
{
    private const decimal BelkaTaxRate = 0.19m;

    /// <summary><see cref="DepositTermUnit.Days"/>: <c>start + n</c> days; <see cref="DepositTermUnit.Months"/>: <c>start.AddMonths(n)</c>, clamped to the month's end.</summary>
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
            // Period ends step from the start date (not from the previous end), so a month-end start
            // keeps landing on month ends; the last one is cut at maturity.
            var periodEnd = stepMonths is { } step
                ? Min(terms.StartDate.AddMonths(k * step), maturityDate)
                : maturityDate;
            var days = periodEnd.DayNumber - periodStart.DayNumber;

            var gross = Math.Round(
                balance * terms.AnnualInterestRatePercent / 100m * days / 365m, 2, MidpointRounding.AwayFromZero);
            var tax = terms.TaxExempt ? 0m : Math.Ceiling(gross * BelkaTaxRate * 100m) / 100m;
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
