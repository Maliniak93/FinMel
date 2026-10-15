using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning;

namespace Skarbiec.Portfolio.Features.RecurringIncomes.DeleteRecurringIncomeChange;

public sealed class DeleteRecurringIncomeChangeHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, string month, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var effective))
        {
            return PlanningErrors.InvalidMonth(month);
        }

        var income = await dbContext.RecurringIncomes
            .Include(i => i.Versions)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (income is null)
        {
            return RecurringIncomeErrors.NotFound(id);
        }

        var version = income.Versions.FirstOrDefault(v => v.EffectiveFrom == effective);
        if (version is null)
        {
            return RecurringIncomeErrors.ChangeNotFound(id, month);
        }

        if (version.EffectiveFrom == income.Versions.Min(v => v.EffectiveFrom))
        {
            return RecurringIncomeErrors.FirstVersion(month);
        }

        dbContext.RecurringIncomeVersions.Remove(version);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
