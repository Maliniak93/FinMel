using Skarbiec.Portfolio.Features.SavingsAccounts;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// savings-interest-settlement: the worked examples of <see cref="SavingsInterestMath"/> - pure, no
/// host, no DB, no clock. A period is a calendar month; the gross interest is the sum of the
/// end-of-day balances x rate / 100 / 365, rounded half away from zero once per period; the tax is
/// the term-deposit Belka rule.
/// </summary>
public sealed class SavingsInterestMathTests
{
    private static readonly DateOnly SeptemberStart = new(2026, 9, 1);
    private static readonly DateOnly SeptemberEnd = new(2026, 9, 30);

    // scenario, top-up on 16 September, rate %, tax exempt, gross, tax, net, average daily balance
    public static TheoryData<string, decimal, decimal, bool, decimal, decimal, decimal, decimal> AccrueCases() => new()
    {
        { "constant 10 000", 0m, 5m, false, 41.10m, 7.81m, 33.29m, 10_000.00m },
        { "5 000 more on 16 September", 5_000m, 5m, false, 51.37m, 9.77m, 41.60m, 12_500.00m },
        { "tax exempt", 0m, 5m, true, 41.10m, 0m, 41.10m, 10_000.00m },
        { "zero rate", 0m, 0m, false, 0m, 0m, 0m, 10_000.00m },
    };

    /// <summary>AC-1: a PLN account over the whole of September, opening deposit of 10 000 on 1 September.</summary>
    [Theory]
    [MemberData(nameof(AccrueCases))]
    public void Accrue_Examples(
        string scenario, decimal topUp, decimal rate, bool taxExempt, decimal gross, decimal tax, decimal net, decimal averageDailyBalance)
    {
        Assert.NotEmpty(scenario);
        var flows = new List<SavingsCashFlow> { new(SeptemberStart, 10_000m) };
        if (topUp > 0m)
        {
            flows.Add(new SavingsCashFlow(new DateOnly(2026, 9, 16), topUp));
        }

        var accrual = SavingsInterestMath.Accrue(SeptemberStart, SeptemberEnd, flows, rate, taxExempt);

        Assert.Equal(gross, accrual.GrossInterest);
        Assert.Equal(tax, accrual.Tax);
        Assert.Equal(net, accrual.NetInterest);
        Assert.Equal(averageDailyBalance, accrual.AverageDailyBalance);
    }

    /// <summary>A withdrawal lowers the end-of-day balance of its own day, and an earlier credit compounds.</summary>
    [Fact]
    public void Accrue_WithdrawAndEarlierCredit_UsesEndOfDayBalances()
    {
        var flows = new List<SavingsCashFlow>
        {
            new(new DateOnly(2026, 8, 1), 10_000m),
            new(new DateOnly(2026, 8, 31), 33.29m), // an earlier interest credit
            new(new DateOnly(2026, 9, 16), -5_000m),
        };

        var accrual = SavingsInterestMath.Accrue(SeptemberStart, SeptemberEnd, flows, 5m, taxExempt: false);

        // 15 days at 10 033.29 + 15 days at 5 033.29
        var sum = (15 * 10_033.29m) + (15 * 5_033.29m);
        Assert.Equal(Math.Round(sum * 5m / 100m / 365m, 2, MidpointRounding.AwayFromZero), accrual.GrossInterest);
        Assert.Equal(Math.Round(sum / 30m, 2, MidpointRounding.AwayFromZero), accrual.AverageDailyBalance);
    }

    // scenario, today, expected period start, expected period end, expected due count
    public static TheoryData<string, DateOnly, DateOnly?, DateOnly?, int> NextDueCases() => new()
    {
        { "first period 16-30 September, not ended on 30 September", new DateOnly(2026, 9, 30), null, null, 0 },
        { "September is due on 1 October", new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 30), 1 },
        { "two months unsettled", new DateOnly(2026, 11, 1), new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 30), 2 },
        { "October follows once September is settled", new DateOnly(2026, 11, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 1 },
        { "a zero-interest month is skipped", new DateOnly(2026, 11, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 1 },
        { "a skipped month is not counted", new DateOnly(2026, 12, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 2 },
    };

    /// <summary>AC-2. Each scenario's history and latest settlement are set up in <see cref="HistoryFor"/>.</summary>
    [Theory]
    [MemberData(nameof(NextDueCases))]
    public void NextDuePeriod_FollowsCalendarMonths(
        string scenario, DateOnly today, DateOnly? expectedStart, DateOnly? expectedEnd, int expectedDueCount)
    {
        var (flows, lastSettledPeriodEnd) = HistoryFor(scenario);

        var due = SavingsInterestMath.NextDuePeriod(flows, 5m, taxExempt: false, lastSettledPeriodEnd, today);

        if (expectedStart is null)
        {
            Assert.Null(due);
            return;
        }

        Assert.NotNull(due);
        Assert.Equal(expectedStart, due.PeriodStart);
        Assert.Equal(expectedEnd, due.PeriodEnd);
        Assert.Equal(expectedDueCount, due.DuePeriodCount);
        Assert.True(due.Accrual.GrossInterest > 0m);
    }

    [Fact]
    public void NextDuePeriod_NoTransactions_HasNoPeriod()
    {
        var due = SavingsInterestMath.NextDuePeriod([], 5m, taxExempt: false, lastSettledPeriodEnd: null, new DateOnly(2027, 1, 1));

        Assert.Null(due);
    }

    [Fact]
    public void NextDuePeriod_ZeroRate_IsNeverDue()
    {
        var flows = new List<SavingsCashFlow> { new(new DateOnly(2026, 9, 16), 10_000m) };

        var due = SavingsInterestMath.NextDuePeriod(flows, 0m, taxExempt: false, lastSettledPeriodEnd: null, new DateOnly(2027, 1, 1));

        Assert.Null(due);
    }

    /// <summary>The due period carries the accrual of its own days.</summary>
    [Fact]
    public void NextDuePeriod_CarriesTheAccrualOfItsPeriod()
    {
        var flows = new List<SavingsCashFlow> { new(SeptemberStart, 10_000m) };

        var due = SavingsInterestMath.NextDuePeriod(flows, 5m, taxExempt: false, lastSettledPeriodEnd: null, new DateOnly(2026, 10, 1));

        Assert.NotNull(due);
        Assert.Equal(SeptemberStart, due.PeriodStart);
        Assert.Equal(SeptemberEnd, due.PeriodEnd);
        Assert.Equal(41.10m, due.Accrual.GrossInterest);
        Assert.Equal(7.81m, due.Accrual.Tax);
        Assert.Equal(33.29m, due.Accrual.NetInterest);
        Assert.Equal(10_000.00m, due.Accrual.AverageDailyBalance);
    }

    private static (IReadOnlyList<SavingsCashFlow> Flows, DateOnly? LastSettledPeriodEnd) HistoryFor(string scenario)
    {
        var sixteenth = new DateOnly(2026, 9, 16);

        return scenario switch
        {
            "October follows once September is settled" =>
                ([new(sixteenth, 10_000m), new(SeptemberEnd, 33.29m)], SeptemberEnd),

            // Deposit and withdrawal on the same day leave nothing in September; October has interest.
            "a zero-interest month is skipped" or "a skipped month is not counted" =>
                ([new(sixteenth, 100m), new(sixteenth, -100m), new(new DateOnly(2026, 10, 5), 10_000m)], null),

            _ => ([new(sixteenth, 10_000m)], null),
        };
    }
}
