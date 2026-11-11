using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

public sealed class PlanningMonthLoader(
    PortfolioDbContext dbContext, IInstrumentQuoteLookupClient quoteLookup, TimeProvider timeProvider)
{
    internal async Task<PlanningMonthData> LoadAsync(YearMonth first, YearMonth last, CancellationToken cancellationToken)
    {
        var incomes = await dbContext.RecurringIncomes
            .AsNoTracking()
            .Include(i => i.Versions)
            .ToListAsync(cancellationToken);

        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        var stored = await dbContext.RecurringIncomeOccurrences
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var expenseRows = await dbContext.RecurringExpenses
            .AsNoTracking()
            .Include(e => e.Versions)
            .ToListAsync(cancellationToken);

        var settledPayments = await dbContext.RecurringExpenseOccurrences
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var oneOffIncomes = await dbContext.OneOffIncomes
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var oneOffExpenses = await dbContext.OneOffExpenses
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var groups = await dbContext.AssetGroups
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var today = WarsawCalendar.Today(timeProvider);
        var currentMonth = YearMonth.From(today);

        var forecast = new PlanningForecast(
            incomes, accounts, stored, expenseRows, settledPayments, oneOffIncomes, oneOffExpenses, groups, currentMonth);

        // A later month's cushion level depends on the free funds of every month from the current one.
        var foreign = new List<string>();
        for (var walked = last > currentMonth && currentMonth < first ? currentMonth : first; walked <= last; walked = walked.AddMonths(1))
        {
            foreign.AddRange(forecast.ForeignIn(walked));
        }

        foreign = foreign.Distinct().ToList();

        var replica = await dbContext.ValuationReplicas.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (foreign.Count > 0)
        {
            var lookup = await quoteLookup.GetRatesAsync(foreign, today, cancellationToken);
            if (lookup.Status == InstrumentQuoteLookupStatus.Found)
            {
                foreach (var (currency, rate) in lookup.Rates!)
                {
                    rates[currency] = rate;
                }
            }
        }

        var settings = await dbContext.PlanningSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        var funding = await LoadFundingAsync(first, last < currentMonth ? last : currentMonth, groups.Count > 0, cancellationToken);

        return new PlanningMonthData(
            today,
            incomes,
            accounts,
            forecast,
            rates,
            replica,
            funding,
            settings?.PaymentLeadDays ?? PlanningSettings.DefaultPaymentLeadDays);
    }

    private async Task<IReadOnlyList<FundingLeg>> LoadFundingAsync(
        YearMonth first, YearMonth last, bool anyGroup, CancellationToken cancellationToken)
    {
        if (!anyGroup || first > last)
        {
            return [];
        }

        var firstDay = first.FirstDay;
        var lastDay = last.LastDay;

        return await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.TransferId != null && t.Date >= firstDay && t.Date <= lastDay)
            .Join(
                dbContext.Assets,
                t => t.AssetId,
                a => a.Id,
                (t, a) => new FundingLeg(
                    t.TransferId, t.Type, t.Date, a.GroupId, a.AccountId, t.Quantity, t.UnitPriceAmount, t.FxRateToPln, t.AmountPln))
            .ToListAsync(cancellationToken);
    }
}
