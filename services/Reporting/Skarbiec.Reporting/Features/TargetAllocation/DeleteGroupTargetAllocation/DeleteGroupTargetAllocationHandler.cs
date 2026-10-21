using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Features.TargetAllocation.DeleteGroupTargetAllocation;

public sealed class DeleteGroupTargetAllocationHandler(ReportingDbContext db)
{
    public async Task<Result> HandleAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var allocation = await db.GroupTargetAllocations.FirstOrDefaultAsync(a => a.GroupId == groupId, cancellationToken);
        if (allocation is not null)
        {
            db.GroupTargetAllocations.Remove(allocation);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
