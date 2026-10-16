using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.Planning.Reserves;

public sealed record ReserveTerms(decimal Amount, MonthlyRecurrence Rule);

public sealed record ReservePlan(DateOnly Start, DateOnly? End, YearMonth CreatedMonth, Func<YearMonth, ReserveTerms> TermsFor);

public sealed record ReservePayment(YearMonth NominalMonth, DateOnly Date, decimal Amount);

public sealed record MonthReserve(YearMonth NominalMonth, DateOnly PaymentDate, decimal Amount);

public static class ReserveSchedule
{
    // A window never exceeds 12 months, so a nominal month is reserved for no earlier than 12 months before it.
    private const int MaxLookBackMonths = 12;

    public static IReadOnlyList<ReservePayment> PaymentsIn(YearMonth month, ReservePlan plan) =>
        RecurrenceSchedule
            .OccurrencesIn(month, plan.Start, plan.End, nominal => plan.TermsFor(nominal).Rule)
            .Select(o => new ReservePayment(o.NominalMonth, o.Date, plan.TermsFor(o.NominalMonth).Amount))
            .ToList();

    public static IReadOnlyList<MonthReserve> ReservesIn(YearMonth month, ReservePlan plan)
    {
        if (month < plan.CreatedMonth)
        {
            return PaymentsIn(month, plan)
                .Select(p => new MonthReserve(p.NominalMonth, p.Date, p.Amount))
                .ToList();
        }

        var windowLength = plan.TermsFor(month).Rule.IntervalMonths;
        var reserves = new List<MonthReserve>();

        for (var offset = -1; offset <= windowLength; offset++)
        {
            var nominal = month.AddMonths(offset);
            var reserved = 0m;
            for (var earlier = Latest(plan.CreatedMonth, nominal.AddMonths(-MaxLookBackMonths)); earlier < month; earlier = earlier.AddMonths(1))
            {
                reserved += ReserveAt(plan, nominal, earlier, reserved)?.Amount ?? 0m;
            }

            if (ReserveAt(plan, nominal, month, reserved) is { } reserve)
            {
                reserves.Add(reserve);
            }
        }

        return reserves;
    }

    public static decimal CostIn(YearMonth month, ReservePlan plan) =>
        ReservesIn(month, plan).Sum(r => r.Amount);

    private static MonthReserve? ReserveAt(ReservePlan plan, YearMonth nominal, YearMonth month, decimal reserved)
    {
        var anchor = YearMonth.From(plan.Start);

        var own = plan.TermsFor(nominal);
        if (own.Rule.IsDue(nominal, anchor))
        {
            var date = own.Rule.DateFor(nominal);
            if (IsInRange(plan, date) && YearMonth.From(date) == month)
            {
                return NonZero(nominal, date, own.Amount - reserved);
            }
        }

        var current = plan.TermsFor(month);
        if (!current.Rule.IsDue(nominal, anchor))
        {
            return null;
        }

        var plannedDate = current.Rule.DateFor(nominal);
        var plannedMonth = YearMonth.From(plannedDate);
        var monthsToPayment = (plannedMonth.Year - month.Year) * 12 + plannedMonth.Month - month.Month;
        if (!IsInRange(plan, plannedDate) || monthsToPayment < 1 || monthsToPayment > current.Rule.IntervalMonths - 1)
        {
            return null;
        }

        var share = Math.Round((current.Amount - reserved) / (monthsToPayment + 1), 2, MidpointRounding.ToZero);
        return NonZero(nominal, plannedDate, share);
    }

    private static MonthReserve? NonZero(YearMonth nominal, DateOnly date, decimal amount) =>
        amount == 0m ? null : new MonthReserve(nominal, date, amount);

    private static bool IsInRange(ReservePlan plan, DateOnly date) =>
        date >= plan.Start && (plan.End is null || date <= plan.End);

    private static YearMonth Latest(YearMonth left, YearMonth right) => left >= right ? left : right;
}
