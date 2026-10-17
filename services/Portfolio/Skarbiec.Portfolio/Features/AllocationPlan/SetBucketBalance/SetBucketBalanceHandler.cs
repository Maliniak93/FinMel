using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning;
using Skarbiec.Portfolio.Features.Planning.Allocation;

namespace Skarbiec.Portfolio.Features.AllocationPlan.SetBucketBalance;

public sealed class SetBucketBalanceHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid bucketId, string month, SetBucketBalanceRequest request, CancellationToken cancellationToken)
    {
        var bucket = await dbContext.AllocationBuckets.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bucketId, cancellationToken);
        if (bucket is null)
        {
            return AllocationPlanErrors.BucketNotFound(bucketId);
        }

        if (!BalanceMonth.TryParse(month, out var firstDay))
        {
            return PlanningErrors.InvalidMonth(month);
        }

        if (bucket.Kind is not (BucketKind.Cushion or BucketKind.Ike or BucketKind.Ikze or BucketKind.IkzeJdg))
        {
            return AllocationPlanErrors.BucketHasNoBalance(bucketId);
        }

        var existing = await dbContext.AllocationBalanceOverrides
            .FirstOrDefaultAsync(o => o.BucketId == bucketId && o.Month == firstDay, cancellationToken);
        if (existing is null)
        {
            dbContext.AllocationBalanceOverrides.Add(new AllocationBalanceOverride
            {
                Id = Guid.NewGuid(),
                BucketId = bucketId,
                Month = firstDay,
                Amount = request.Amount
            });
        }
        else
        {
            existing.Amount = request.Amount;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
