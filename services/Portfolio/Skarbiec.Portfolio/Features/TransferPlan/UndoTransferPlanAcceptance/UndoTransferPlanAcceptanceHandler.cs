using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;

namespace Skarbiec.Portfolio.Features.TransferPlan.UndoTransferPlanAcceptance;

public sealed class UndoTransferPlanAcceptanceHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(string month, CancellationToken cancellationToken)
    {
        var parsed = OccurrenceRules.ParseMonth(month);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        var first = parsed.Value.FirstDay;
        var plan = await dbContext.TransferPlans
            .Include(p => p.Batches)
            .ThenInclude(b => b.Rows)
            .FirstOrDefaultAsync(p => p.Month == first && !p.IsSkipped, cancellationToken);
        if (plan is null)
        {
            return TransferPlanErrors.NotFound(month);
        }

        var latest = plan.Batches.MaxBy(b => b.Number)!;
        if (latest.Rows.Any(r => r.CheckedAtUtc is not null))
        {
            return TransferPlanErrors.RowChecked(month);
        }

        if (plan.Batches.Count == 1)
        {
            dbContext.TransferPlans.Remove(plan);
        }
        else
        {
            dbContext.TransferPlanBatches.Remove(latest);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
