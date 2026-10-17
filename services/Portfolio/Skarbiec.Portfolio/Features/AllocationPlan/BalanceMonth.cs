using System.Globalization;
using Skarbiec.Portfolio.Features.RecurringIncomes;

namespace Skarbiec.Portfolio.Features.AllocationPlan;

internal static class BalanceMonth
{
    public static bool TryParse(string month, out DateOnly firstDay) =>
        DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out firstDay)
        && firstDay >= RecurringIncomeLimits.FirstDay
        && firstDay <= RecurringIncomeLimits.LastDay;
}
