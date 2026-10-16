using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning;

namespace Skarbiec.Portfolio.Features.RecurringExpenses.DeleteRecurringExpenseChange;

public sealed class DeleteRecurringExpenseChangeHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, string month, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var effective))
        {
            return PlanningErrors.InvalidMonth(month);
        }

        var expense = await dbContext.RecurringExpenses
            .Include(e => e.Versions)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (expense is null)
        {
            return RecurringExpenseErrors.NotFound(id);
        }

        var version = expense.Versions.FirstOrDefault(v => v.EffectiveFrom == effective);
        if (version is null)
        {
            return RecurringExpenseErrors.ChangeNotFound(id, month);
        }

        if (version.EffectiveFrom == expense.Versions.Min(v => v.EffectiveFrom))
        {
            return RecurringExpenseErrors.FirstVersion(month);
        }

        dbContext.RecurringExpenseVersions.Remove(version);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
