using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.Planning;
using Skarbiec.Portfolio.Features.Planning.Allocation;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.RecurringIncomes;

namespace Skarbiec.Portfolio.Features.AllocationPlan.SaveAllocationPlanVersion;

public sealed class SaveAllocationPlanVersionHandler(PortfolioDbContext dbContext)
{
    public async Task<Result> HandleAsync(string month, SaveAllocationPlanVersionRequest request, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || parsed < RecurringIncomeLimits.FirstDay
            || parsed > RecurringIncomeLimits.LastDay)
        {
            return PlanningErrors.InvalidMonth(month);
        }

        var effective = YearMonth.From(parsed);
        var rows = request.Buckets;

        if (rows.Where(r => r.ShareType == BucketShareType.Percent).Sum(r => r.Share ?? 0m) > 100m)
        {
            return AllocationPlanErrors.PercentSumOver100;
        }

        var requestedBucketIds = rows.Where(r => r.BucketId is not null).Select(r => r.BucketId!.Value).ToList();
        var buckets = await dbContext.AllocationBuckets
            .AsNoTracking()
            .Where(b => requestedBucketIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        foreach (var row in rows.Where(r => r.BucketId is not null))
        {
            if (!buckets.TryGetValue(row.BucketId!.Value, out var existing))
            {
                return AllocationPlanErrors.BucketNotFound(row.BucketId.Value);
            }

            if (existing.Kind != row.Kind)
            {
                return AllocationPlanErrors.BucketKindMismatch(existing.Id);
            }
        }

        var versions = await dbContext.AllocationPlanVersions
            .Include(v => v.Entries)
            .ToListAsync(cancellationToken);
        var inForce = versions.InForce(effective);
        var inForceAccounts = inForce?.Entries.ToDictionary(e => e.BucketId, e => e.AccountId) ?? [];

        var requestedAccountIds = rows.Where(r => r.AccountId is not null).Select(r => r.AccountId!.Value).Distinct().ToList();
        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .Where(a => requestedAccountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        foreach (var row in rows.Where(r => r.AccountId is not null))
        {
            var accountId = row.AccountId!.Value;
            if (!accounts.TryGetValue(accountId, out var account))
            {
                return AccountErrors.NotFound(accountId);
            }

            var unchanged = row.BucketId is { } bucketId
                && inForceAccounts.TryGetValue(bucketId, out var current)
                && current == accountId;
            if (account.IsArchived && !unchanged)
            {
                return AccountErrors.Archived(accountId);
            }
        }

        var version = versions.FirstOrDefault(v => v.EffectiveFrom == effective.FirstDay);
        if (version is null)
        {
            version = new AllocationPlanVersion { Id = Guid.NewGuid(), EffectiveFrom = effective.FirstDay };
            dbContext.AllocationPlanVersions.Add(version);
        }
        else
        {
            dbContext.AllocationPlanEntries.RemoveRange(version.Entries);
        }

        var positions = new Dictionary<BucketShareType, int>();
        var keptBucketIds = new HashSet<Guid>();
        foreach (var row in rows)
        {
            var bucketId = row.BucketId ?? Guid.NewGuid();
            if (row.BucketId is null)
            {
                dbContext.AllocationBuckets.Add(new AllocationBucket { Id = bucketId, Kind = row.Kind });
                if (row.Kind == BucketKind.Cushion)
                {
                    dbContext.AllocationBalanceOverrides.Add(new AllocationBalanceOverride
                    {
                        Id = Guid.NewGuid(),
                        BucketId = bucketId,
                        Month = effective.FirstDay,
                        Amount = row.StartingBalance ?? 0m
                    });
                }
            }

            keptBucketIds.Add(bucketId);
            var position = 0;
            if (row.ShareType is { } shareType)
            {
                position = positions.GetValueOrDefault(shareType);
                positions[shareType] = position + 1;
            }

            dbContext.AllocationPlanEntries.Add(new AllocationPlanEntry
            {
                Id = Guid.NewGuid(),
                VersionId = version.Id,
                BucketId = bucketId,
                Name = row.Name.Trim(),
                ShareType = row.ShareType,
                Share = row.Share,
                Position = position,
                IsRemainder = row.IsRemainder,
                AccountId = row.AccountId,
                TargetMonths = row.TargetMonths
            });
        }

        foreach (var other in versions.Where(v => v.Id != version.Id))
        {
            keptBucketIds.UnionWith(other.Entries.Select(e => e.BucketId));
        }

        var orphans = await dbContext.AllocationBuckets
            .Where(b => !keptBucketIds.Contains(b.Id))
            .ToListAsync(cancellationToken);
        dbContext.AllocationBuckets.RemoveRange(orphans);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
