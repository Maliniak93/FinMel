using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.SavingsAccounts;

/// <summary>One movement of a savings account's balance: + for a Deposit, − for a Withdraw.</summary>
public sealed record SavingsCashFlow(DateOnly Date, decimal Amount)
{
    /// <summary>The flow of a stored transaction — its quantity delta, since a savings account's unit price is 1.</summary>
    public static SavingsCashFlow From(Transaction transaction) =>
        new(transaction.Date, TransactionQuantityCalculator.QuantityDelta(transaction));
}

/// <summary>The interest of one period, projected from its end-of-day balances.</summary>
public sealed record SavingsInterestAccrual
{
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }
    public required decimal AverageDailyBalance { get; init; }
}

/// <summary>The next period to settle, with how many ended, unsettled periods carry interest.</summary>
public sealed record SavingsInterestDuePeriod
{
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required SavingsInterestAccrual Accrual { get; init; }

    /// <summary>Ended, unsettled periods with a projected gross above 0 — this one included.</summary>
    public required int DuePeriodCount { get; init; }
}

/// <summary>
/// Pure savings-account interest arithmetic (savings-interest-settlement) — no I/O, no clock. Every
/// calendar month is a period, the first one starting on the earliest transaction's date. A period's
/// gross is <c>Σ B(d) × rate / 100 / 365</c> over its days, with <c>B(d)</c> the end-of-day balance
/// (earlier credits included, so interest compounds monthly), rounded half away from zero once per
/// period; the tax is <see cref="BelkaTax"/>.
/// </summary>
public static class SavingsInterestMath
{
    public static SavingsInterestAccrual Accrue(
        DateOnly periodStart, DateOnly periodEnd, IReadOnlyList<SavingsCashFlow> flows, decimal annualRatePercent, bool taxExempt)
    {
        var balance = 0m;
        var inPeriod = new Dictionary<DateOnly, decimal>();
        foreach (var flow in flows)
        {
            if (flow.Date < periodStart)
            {
                balance += flow.Amount;
            }
            else if (flow.Date <= periodEnd)
            {
                inPeriod[flow.Date] = inPeriod.GetValueOrDefault(flow.Date) + flow.Amount;
            }
        }

        var balanceSum = 0m;
        for (var day = periodStart; day <= periodEnd; day = day.AddDays(1))
        {
            balance += inPeriod.GetValueOrDefault(day);
            balanceSum += balance;
        }

        var days = periodEnd.DayNumber - periodStart.DayNumber + 1;
        var gross = Math.Round(balanceSum * annualRatePercent / 100m / 365m, 2, MidpointRounding.AwayFromZero);
        var tax = BelkaTax.On(gross, taxExempt);

        return new SavingsInterestAccrual
        {
            GrossInterest = gross,
            Tax = tax,
            NetInterest = gross - tax,
            AverageDailyBalance = Math.Round(balanceSum / days, 2, MidpointRounding.AwayFromZero)
        };
    }

    /// <summary>
    /// The first ended period after <paramref name="lastSettledPeriodEnd"/> (or from the first period)
    /// whose projected gross is above 0, or <see langword="null"/> when none is. A period has ended
    /// when <c>today &gt; PeriodEnd</c>; a zero-interest period is skipped and not counted.
    /// </summary>
    public static SavingsInterestDuePeriod? NextDuePeriod(
        IReadOnlyList<SavingsCashFlow> flows,
        decimal annualRatePercent,
        bool taxExempt,
        DateOnly? lastSettledPeriodEnd,
        DateOnly today)
    {
        if (flows.Count == 0)
        {
            return null;
        }

        var firstDate = flows.Min(f => f.Date);
        var periodStart = lastSettledPeriodEnd is { } settled && settled >= firstDate
            ? settled.AddDays(1)
            : firstDate;

        DateOnly? dueStart = null;
        DateOnly? dueEnd = null;
        SavingsInterestAccrual? dueAccrual = null;
        var dueCount = 0;

        for (var periodEnd = MonthEnd(periodStart); today > periodEnd; periodStart = periodEnd.AddDays(1), periodEnd = MonthEnd(periodStart))
        {
            var accrual = Accrue(periodStart, periodEnd, flows, annualRatePercent, taxExempt);
            if (accrual.GrossInterest <= 0m)
            {
                continue;
            }

            dueCount++;
            if (dueAccrual is null)
            {
                dueStart = periodStart;
                dueEnd = periodEnd;
                dueAccrual = accrual;
            }
        }

        return dueAccrual is null
            ? null
            : new SavingsInterestDuePeriod
            {
                PeriodStart = dueStart!.Value,
                PeriodEnd = dueEnd!.Value,
                Accrual = dueAccrual,
                DuePeriodCount = dueCount
            };
    }

    private static DateOnly MonthEnd(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}
