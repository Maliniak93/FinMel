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
    private const int MaxNameLength = 50;

    public async Task<Result> HandleAsync(SaveTargetAllocationRequest request, CancellationToken cancellationToken)
    {
        var groups = request.Groups
            .Select(g => new TargetAllocationGroup(g.Name?.Trim() ?? string.Empty, g.AssetClasses?.ToList() ?? [], g.TargetPercent, g.BandPp))
            .ToList();

        var error = Validate(groups);
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

    private static Error? Validate(List<TargetAllocationGroup> groups)
    {
        foreach (var group in groups)
        {
            if (group.Name.Length is 0 or > MaxNameLength)
            {
                return TargetAllocationErrors.InvalidGroup($"A group name must be 1-{MaxNameLength} characters.");
            }

            if (!IsOneDecimalPercent(group.TargetPercent) || !IsOneDecimalPercent(group.BandPp))
            {
                return TargetAllocationErrors.InvalidGroup($"Group '{group.Name}': target and band must be 0-100 with at most one decimal.");
            }

            if (group.AssetClasses.Any(c => !Enum.IsDefined(c)))
            {
                return TargetAllocationErrors.InvalidGroup($"Group '{group.Name}' has an undefined asset class.");
            }
        }

        var empty = groups.FirstOrDefault(g => g.AssetClasses.Count == 0);
        if (empty is not null)
        {
            return TargetAllocationErrors.GroupWithoutAssetClass(empty.Name);
        }

        if (groups.Select(g => g.Name.ToUpperInvariant()).Distinct().Count() != groups.Count)
        {
            return TargetAllocationErrors.DuplicateGroupName;
        }

        if (groups.SelectMany(g => g.AssetClasses).GroupBy(c => c).Any(c => c.Count() > 1))
        {
            return TargetAllocationErrors.AssetClassInManyGroups;
        }

        return groups.Sum(g => g.TargetPercent) == 100m ? null : TargetAllocationErrors.Sum;
    }

    private static bool IsOneDecimalPercent(decimal value) =>
        value is >= 0m and <= 100m && value == Math.Round(value, 1);
}
