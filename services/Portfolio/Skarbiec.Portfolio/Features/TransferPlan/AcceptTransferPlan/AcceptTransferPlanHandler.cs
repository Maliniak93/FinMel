using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;
using StoredTransferPlan = Skarbiec.Portfolio.Data.TransferPlan;

namespace Skarbiec.Portfolio.Features.TransferPlan.AcceptTransferPlan;

public sealed class AcceptTransferPlanHandler(
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

        var preview = planning.Value.TransferPlan;
        if (preview.Status != TransferPlanStatus.Preview)
        {
            return TransferPlanErrors.NotPreview(month);
        }

        var plan = new StoredTransferPlan { Id = Guid.NewGuid(), Month = parsed.Value.FirstDay, IsSkipped = false };
        plan.Batches.Add(await TransferPlanBatchFactory.CreateAsync(
            dbContext,
            plan.Id,
            1,
            preview.Rows,
            preview.ValuationDate,
            preview.MinimumTransferPln,
            timeProvider.GetUtcNow(),
            cancellationToken));

        dbContext.TransferPlans.Add(plan);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
