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

        var oneOffIncomes = await dbContext.OneOffIncomes
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var oneOffExpenses = await dbContext.OneOffExpenses
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var forecast = new PlanningForecast(incomes, accounts, stored, expenseRows, oneOffIncomes, oneOffExpenses);

        var foreign = new List<string>();
        for (var walked = first; walked <= last; walked = walked.AddMonths(1))
        {
            foreign.AddRange(forecast.ForeignIn(walked));
        }

        foreign = foreign.Distinct().ToList();

        var today = WarsawCalendar.Today(timeProvider);
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

        return new PlanningMonthData(
            today,
            incomes,
            accounts,
            forecast,
            rates,
            settings?.PaymentLeadDays ?? PlanningSettings.DefaultPaymentLeadDays);
    }
}
