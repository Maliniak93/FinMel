using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;
using Skarbiec.ServiceDefaults.Authentication;
using AllocationEntity = Skarbiec.Reporting.Data.TargetAllocation;

namespace Skarbiec.Reporting.Features.TargetAllocation.SaveTargetAllocation;

public sealed class SaveTargetAllocationHandler(
    ReportingDbContext db, ICurrentUser currentUser, TimeProvider timeProvider, IPublishEndpoint publishEndpoint)
{
    public async Task<Result> HandleAsync(SaveTargetAllocationRequest request, CancellationToken cancellationToken)
    {
        var groups = TargetAllocationRules.Normalize(request);

        var error = TargetAllocationRules.Validate(groups);
        if (error is not null)
        {
            return error;
        }

        var savedAt = timeProvider.GetUtcNow();
        var allocation = await db.TargetAllocations.FirstOrDefaultAsync(cancellationToken);
        if (allocation is null)
        {
            db.TargetAllocations.Add(new AllocationEntity
            {
                UserId = currentUser.UserId,
                UpdatedAt = savedAt,
                Groups = groups,
            });
        }
        else
        {
            allocation.Groups = groups;
            allocation.UpdatedAt = savedAt;
        }

        await publishEndpoint.Publish(
            new TargetAllocationSaved
            {
                UserId = currentUser.UserId,
                Groups =
                [
                    .. groups.Select(g => new TargetAllocationGroupState
                    {
                        Name = g.Name,
                        AssetClasses = g.AssetClasses,
                        TargetPercent = g.TargetPercent,
                        BandPp = g.BandPp,
                    }),
                ],
                SavedAtUtc = savedAt,
            },
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
