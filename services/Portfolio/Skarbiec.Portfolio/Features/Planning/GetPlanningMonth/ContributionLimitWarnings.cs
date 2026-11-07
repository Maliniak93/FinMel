using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

internal static class ContributionLimitWarnings
{
    public static async Task<IReadOnlyList<ContributionLimitWarningResponse>> LoadAsync(
        PortfolioDbContext dbContext, PlanningMonthData data, int year, CancellationToken cancellationToken)
    {
        var wrapped = data.Accounts.Values
            .Where(a => !a.IsArchived && a.Wrapper != TaxWrapper.Regular)
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (wrapped.Count == 0)
        {
            return [];
        }

        var cashByAccount = await dbContext.Assets
            .AsNoTracking()
            .Where(a => a.AccountId != null && (a.AssetClass == AssetClass.Cash || a.AssetClass == AssetClass.BankAccount))
            .ToDictionaryAsync(a => a.AccountId!.Value, cancellationToken);

        var contributions = await dbContext.LoadContributionsAsync(wrapped, cashByAccount, year, cancellationToken);
        var limited = wrapped
            .Where(a => contributions.TryGetValue(a.Id, out var c) && c.LimitPln is not null)
            .ToList();
        if (limited.Count == 0)
        {
            return [];
        }

        var planned = PlannedByAccount(data, year);

        return limited
            .Select(account =>
            {
                var contribution = contributions[account.Id];
                var plannedPln = planned.GetValueOrDefault(account.Id);
                var limit = contribution.LimitPln!.Value;

                return new ContributionLimitWarningResponse
                {
                    AccountId = account.Id,
                    AccountName = account.Name,
                    Wrapper = account.Wrapper,
                    Year = year,
                    LimitPln = limit,
                    ContributedPln = contribution.ContributedPln,
                    PlannedPln = plannedPln,
                    ExceededByPln = contribution.ContributedPln + plannedPln - limit
                };
            })
            .Where(w => w.ExceededByPln > 0m)
            .ToList();
    }

    private static Dictionary<Guid, decimal> PlannedByAccount(PlanningMonthData data, int year)
    {
        var forecast = data.Forecast;
        var recurring = Enumerable.Range(1, 12)
            .SelectMany(month => forecast.OccurrencesIn(new YearMonth(year, month), data.Rates, data.Today))
            .Where(o => o.Status == OccurrenceStatus.Pending)
            .Select(o => (AccountId: o.PlannedAccountId, o.AmountPln));

        var oneOffs = forecast.OneOffIncomesIn(new YearMonth(year, 1), data.Rates, data.Today)
            .Where(o => o.Status == OccurrenceStatus.Pending && o.Date.Year == year)
            .Select(o => (o.AccountId, o.AmountPln));

        return recurring
            .Concat(oneOffs)
            .GroupBy(p => p.AccountId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.AmountPln ?? 0m));
    }
}
