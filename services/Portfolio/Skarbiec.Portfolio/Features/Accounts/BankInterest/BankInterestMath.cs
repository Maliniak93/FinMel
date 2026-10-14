using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.BankInterest;

public sealed record BankCashFlow(DateOnly Date, decimal Amount)
{
    public static BankCashFlow From(Transaction transaction) =>
        new(transaction.Date, TransactionQuantityCalculator.QuantityDelta(transaction));
}

public sealed record BankInterestAccrual
{
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }
    public required decimal AverageDailyBalance { get; init; }
}

public sealed record BankInterestDuePeriod
{
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required BankInterestAccrual Accrual { get; init; }

    public required int DuePeriodCount { get; init; }
}

public static class BankInterestMath
{
    public static BankInterestAccrual Accrue(
        DateOnly periodStart, DateOnly periodEnd, IReadOnlyList<BankCashFlow> flows, decimal annualRatePercent)
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
        var tax = BelkaTax.On(gross, taxExempt: false);

        return new BankInterestAccrual
        {
            GrossInterest = gross,
            Tax = tax,
            NetInterest = gross - tax,
            AverageDailyBalance = Math.Round(balanceSum / days, 2, MidpointRounding.AwayFromZero)
        };
    }

    public static BankInterestDuePeriod? NextDuePeriod(
        IReadOnlyList<BankCashFlow> flows,
        decimal annualRatePercent,
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
        BankInterestAccrual? dueAccrual = null;
        var dueCount = 0;

        for (var periodEnd = MonthEnd(periodStart); today > periodEnd; periodStart = periodEnd.AddDays(1), periodEnd = MonthEnd(periodStart))
        {
            var accrual = Accrue(periodStart, periodEnd, flows, annualRatePercent);
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
            : new BankInterestDuePeriod
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
