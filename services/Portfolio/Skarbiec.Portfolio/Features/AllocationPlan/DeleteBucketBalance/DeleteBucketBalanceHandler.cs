using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning;

namespace Skarbiec.Portfolio.Features.AllocationPlan.DeleteBucketBalance;

public sealed class DeleteBucketBalanceHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid bucketId, string month, CancellationToken cancellationToken)
    {
        if (!await dbContext.AllocationBuckets.AnyAsync(b => b.Id == bucketId, cancellationToken))
        {
            return AllocationPlanErrors.BucketNotFound(bucketId);
        }

        if (!BalanceMonth.TryParse(month, out var firstDay))
        {
            return PlanningErrors.InvalidMonth(month);
        }

        var existing = await dbContext.AllocationBalanceOverrides
            .FirstOrDefaultAsync(o => o.BucketId == bucketId && o.Month == firstDay, cancellationToken);
        if (existing is null)
        {
            return AllocationPlanErrors.BalanceAdjustmentNotFound(bucketId, month);
        }

        dbContext.AllocationBalanceOverrides.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
