using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Features.TargetAllocation.SaveTargetAllocation;
using Skarbiec.ServiceDefaults.Authentication;

namespace Skarbiec.Reporting.Features.TargetAllocation.SaveGroupTargetAllocation;

public sealed class SaveGroupTargetAllocationHandler(ReportingDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(Guid groupId, SaveTargetAllocationRequest request, CancellationToken cancellationToken)
    {
        var groups = TargetAllocationRules.Normalize(request);

        var error = TargetAllocationRules.Validate(groups);
        if (error is not null)
        {
            return error;
        }

        var savedAt = timeProvider.GetUtcNow();
        var allocation = await db.GroupTargetAllocations.FirstOrDefaultAsync(a => a.GroupId == groupId, cancellationToken);
        if (allocation is null)
        {
            db.GroupTargetAllocations.Add(new GroupTargetAllocation
            {
                UserId = currentUser.UserId,
                GroupId = groupId,
                UpdatedAt = savedAt,
                Groups = groups,
            });
        }
        else
        {
            allocation.Groups = groups;
            allocation.UpdatedAt = savedAt;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
