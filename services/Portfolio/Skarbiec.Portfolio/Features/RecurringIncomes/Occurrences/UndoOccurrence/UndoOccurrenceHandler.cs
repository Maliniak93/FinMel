using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences.UndoOccurrence;

public sealed class UndoOccurrenceHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, string month, CancellationToken cancellationToken)
    {
        var parsed = OccurrenceRules.ParseMonth(month);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        if (!await dbContext.RecurringIncomes.AnyAsync(i => i.Id == id, cancellationToken))
        {
            return RecurringIncomeErrors.NotFound(id);
        }

        var stored = await dbContext.RecurringIncomeOccurrences
            .FirstOrDefaultAsync(o => o.RecurringIncomeId == id && o.NominalMonth == parsed.Value.FirstDay, cancellationToken);
        if (stored is null)
        {
            return OccurrenceErrors.NotFound(id, month);
        }

        dbContext.RecurringIncomeOccurrences.Remove(stored);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
