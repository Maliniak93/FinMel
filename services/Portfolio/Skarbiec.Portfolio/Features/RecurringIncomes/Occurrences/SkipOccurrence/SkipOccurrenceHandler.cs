using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences.SkipOccurrence;

public sealed class SkipOccurrenceHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, string month, CancellationToken cancellationToken)
    {
        var parsed = OccurrenceRules.ParseMonth(month);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var nominal = parsed.Value;

        var income = await dbContext.RecurringIncomes
            .Include(i => i.Versions)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (income is null)
        {
            return RecurringIncomeErrors.NotFound(id);
        }

        var stored = await dbContext.RecurringIncomeOccurrences
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.RecurringIncomeId == id && o.NominalMonth == nominal.FirstDay, cancellationToken);
        if (stored is not null)
        {
            return stored.Status == OccurrenceStatus.Settled ? OccurrenceErrors.Settled(month) : Result.Success();
        }

        var skipped = income.Freeze(nominal, OccurrenceStatus.Skipped);
        if (skipped is null)
        {
            return OccurrenceErrors.NotFound(id, month);
        }

        dbContext.RecurringIncomeOccurrences.Add(skipped);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
