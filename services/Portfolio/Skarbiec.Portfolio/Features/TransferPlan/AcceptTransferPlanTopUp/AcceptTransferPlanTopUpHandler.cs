using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;

namespace Skarbiec.Portfolio.Features.TransferPlan.AcceptTransferPlanTopUp;

public sealed class AcceptTransferPlanTopUpHandler(
    PortfolioDbContext dbContext, GetPlanningMonthHandler planningMonth, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(string month, CancellationToken cancellationToken)
    {
        var parsed = OccurrenceRules.ParseMonth(month);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        // The server computes the rows itself: a stale client can never freeze numbers it would not produce.
        var planning = await planningMonth.HandleAsync(month, cancellationToken);
        if (planning.IsFailure)
        {
            return planning.Error;
        }

        var topUp = planning.Value.TransferPlan.TopUp;
        if (topUp is null)
        {
            return TransferPlanErrors.NoTopUp(month);
        }

        var plan = await dbContext.TransferPlans
            .Include(p => p.Batches)
            .FirstOrDefaultAsync(p => p.Month == parsed.Value.FirstDay && !p.IsSkipped, cancellationToken);
        if (plan is null)
        {
            return TransferPlanErrors.NoTopUp(month);
        }

        var batch = await TransferPlanBatchFactory.CreateAsync(
            dbContext,
            plan.Id,
            plan.Batches.Max(b => b.Number) + 1,
            topUp.Rows,
            topUp.ValuationDate,
            topUp.MinimumTransferPln,
            timeProvider.GetUtcNow(),
            cancellationToken);

        dbContext.TransferPlanBatches.Add(batch);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
