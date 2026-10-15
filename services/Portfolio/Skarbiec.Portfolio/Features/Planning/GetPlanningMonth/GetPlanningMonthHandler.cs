using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.CashPlaces;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.RecurringIncomes;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

public sealed class GetPlanningMonthHandler(
    PortfolioDbContext dbContext, IInstrumentQuoteLookupClient quoteLookup, TimeProvider timeProvider)
{
    private static readonly YearMonth FirstMonth = YearMonth.From(RecurringIncomeLimits.FirstDay);
    private static readonly YearMonth LastMonth = YearMonth.From(RecurringIncomeLimits.LastDay);

    public async Task<Result<PlanningMonthResponse>> HandleAsync(string month, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return PlanningErrors.InvalidMonth(month);
        }

        var viewed = YearMonth.From(parsed);
        if (viewed < FirstMonth || viewed > LastMonth)
        {
            return PlanningErrors.InvalidMonth(month);
        }

        var incomes = await dbContext.RecurringIncomes
            .AsNoTracking()
            .Include(i => i.Versions)
            .ToListAsync(cancellationToken);

        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        var planned = incomes
            .SelectMany(income => RecurrenceSchedule
                .OccurrencesIn(viewed, income.StartDate, income.EndDate, nominal => income.InForce(nominal).ToRule())
                .Select(occurrence => (Income: income, Occurrence: occurrence, Version: income.InForce(occurrence.NominalMonth))))
            .OrderBy(p => p.Occurrence.Date)
            .ThenBy(p => p.Income.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var foreign = planned
            .Select(p => accounts[p.Version.AccountId].Currency)
            .Where(c => c != CashPlaceBalances.BaseCurrency)
            .Distinct()
            .ToList();

        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (foreign.Count > 0)
        {
            var lookup = await quoteLookup.GetRatesAsync(foreign, WarsawCalendar.Today(timeProvider), cancellationToken);
            if (lookup.Status == InstrumentQuoteLookupStatus.Found)
            {
                foreach (var (currency, rate) in lookup.Rates!)
                {
                    rates[currency] = rate;
                }
            }
        }

        var occurrences = planned
            .Select(p =>
            {
                var account = accounts[p.Version.AccountId];
                var estimated = account.Currency != CashPlaceBalances.BaseCurrency;

                return new IncomeOccurrenceResponse
                {
                    RecurringIncomeId = p.Income.Id,
                    NominalMonth = p.Occurrence.NominalMonth.ToString(),
                    Date = p.Occurrence.Date,
                    Name = p.Income.Name,
                    AccountId = account.Id,
                    AccountName = account.Name,
                    AccountArchived = account.IsArchived,
                    Amount = p.Version.Amount,
                    Currency = account.Currency,
                    AmountPln = !estimated ? p.Version.Amount
                        : rates.TryGetValue(account.Currency, out var rate) ? Round(p.Version.Amount * rate)
                        : null,
                    Estimated = estimated
                };
            })
            .ToList();

        var definitions = incomes
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(income =>
            {
                var version = income.InForce(viewed);
                var account = accounts[version.AccountId];
                var next = income.Versions
                    .Where(v => v.EffectiveFrom > viewed.FirstDay)
                    .OrderBy(v => v.EffectiveFrom)
                    .FirstOrDefault();
                var nextAccount = next is null ? null : accounts[next.AccountId];

                return new IncomeDefinitionResponse
                {
                    Id = income.Id,
                    Name = income.Name,
                    StartDate = income.StartDate,
                    EndDate = income.EndDate,
                    Status = income.StartDate > viewed.LastDay ? RecurringIncomeStatus.Upcoming
                        : income.EndDate < viewed.FirstDay ? RecurringIncomeStatus.Ended
                        : RecurringIncomeStatus.Active,
                    Amount = version.Amount,
                    Currency = account.Currency,
                    AccountId = account.Id,
                    AccountName = account.Name,
                    AccountArchived = account.IsArchived,
                    IntervalMonths = version.IntervalMonths,
                    DayKind = version.DayKind,
                    DayOfMonth = version.DayOfMonth,
                    Shift = version.Shift,
                    EffectiveFrom = YearMonth.From(version.EffectiveFrom).ToString(),
                    NextChange = next is null || nextAccount is null
                        ? null
                        : new IncomeNextChangeResponse
                        {
                            EffectiveFrom = YearMonth.From(next.EffectiveFrom).ToString(),
                            Amount = next.Amount,
                            Currency = nextAccount.Currency,
                            AccountName = nextAccount.Name,
                            IntervalMonths = next.IntervalMonths,
                            DayKind = next.DayKind,
                            DayOfMonth = next.DayOfMonth,
                            Shift = next.Shift
                        }
                };
            })
            .ToList();

        return new PlanningMonthResponse
        {
            Incomes = new PlanningIncomesResponse
            {
                Occurrences = occurrences,
                TotalPln = occurrences.Sum(o => o.AmountPln ?? 0m),
                FxUnavailable = foreign.Any(c => !rates.ContainsKey(c)),
                Definitions = definitions
            }
        };
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
