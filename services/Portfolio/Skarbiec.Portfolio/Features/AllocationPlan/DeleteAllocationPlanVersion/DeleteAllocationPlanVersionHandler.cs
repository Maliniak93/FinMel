using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning;

namespace Skarbiec.Portfolio.Features.AllocationPlan.DeleteAllocationPlanVersion;

public sealed class DeleteAllocationPlanVersionHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(string month, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var effective))
        {
            return PlanningErrors.InvalidMonth(month);
        }

        var version = await dbContext.AllocationPlanVersions
            .Include(v => v.Entries)
            .FirstOrDefaultAsync(v => v.EffectiveFrom == effective, cancellationToken);
        if (version is null)
        {
            return AllocationPlanErrors.VersionNotFound(month);
        }

        var stillUsed = await dbContext.AllocationPlanEntries
            .Where(e => e.VersionId != version.Id)
            .Select(e => e.BucketId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var orphans = await dbContext.AllocationBuckets
            .Where(b => !stillUsed.Contains(b.Id))
            .ToListAsync(cancellationToken);

        dbContext.AllocationPlanEntries.RemoveRange(version.Entries);
        dbContext.AllocationPlanVersions.Remove(version);
        dbContext.AllocationBuckets.RemoveRange(orphans);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
