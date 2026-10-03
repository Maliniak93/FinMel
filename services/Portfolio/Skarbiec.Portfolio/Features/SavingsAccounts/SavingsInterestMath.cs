using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.SavingsAccounts;

public sealed record SavingsCashFlow(DateOnly Date, decimal Amount)
{
    public static SavingsCashFlow From(Transaction transaction) =>
        new(transaction.Date, TransactionQuantityCalculator.QuantityDelta(transaction));
}

public sealed record SavingsInterestAccrual
{
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }
    public required decimal AverageDailyBalance { get; init; }
}

public sealed record SavingsInterestDuePeriod
{
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required SavingsInterestAccrual Accrual { get; init; }

    public required int DuePeriodCount { get; init; }
}

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
