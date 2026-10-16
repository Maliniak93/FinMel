using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.RecurringIncomes;

namespace Skarbiec.Portfolio.Features.Planning.GetOverdueOccurrences;

public sealed class GetOverdueOccurrencesHandler(PortfolioDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<Result<OverdueOccurrencesResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var today = WarsawCalendar.Today(timeProvider);

        var incomes = await dbContext.RecurringIncomes
            .AsNoTracking()
            .Include(i => i.Versions)
            .Where(i => i.CreatedOn < today)
            .ToListAsync(cancellationToken);

        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        // Stored occurrences are settled or skipped, so a stored key is never overdue.
        var storedKeys = (await dbContext.RecurringIncomeOccurrences
                .AsNoTracking()
                .Select(o => new { o.RecurringIncomeId, o.NominalMonth })
                .ToListAsync(cancellationToken))
            .Select(o => (o.RecurringIncomeId, o.NominalMonth))
            .ToHashSet();

        var last = YearMonth.From(today);
        var items = new List<OverdueOccurrenceResponse>();

        foreach (var income in incomes)
        {
            for (var month = YearMonth.From(income.CreatedOn); month <= last; month = month.AddMonths(1))
            {
                var occurrences = RecurrenceSchedule.OccurrencesIn(
                    month, income.StartDate, income.EndDate, nominal => income.InForce(nominal).ToRule());

                foreach (var occurrence in occurrences)
                {
                    if (occurrence.Date < income.CreatedOn || occurrence.Date >= today
                        || storedKeys.Contains((income.Id, occurrence.NominalMonth.FirstDay)))
                    {
                        continue;
                    }

                    var version = income.InForce(occurrence.NominalMonth);
                    var account = accounts[version.AccountId];
                    items.Add(new OverdueOccurrenceResponse
                    {
                        RecurringIncomeId = income.Id,
                        NominalMonth = occurrence.NominalMonth.ToString(),
                        Date = occurrence.Date,
                        Name = income.Name,
                        AccountId = account.Id,
                        AccountName = account.Name,
                        AccountArchived = account.IsArchived,
                        Amount = version.Amount,
                        Currency = account.Currency
                    });
                }
            }
        }

        return new OverdueOccurrencesResponse
        {
            Items = items
                .OrderBy(i => i.Date)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }
}
