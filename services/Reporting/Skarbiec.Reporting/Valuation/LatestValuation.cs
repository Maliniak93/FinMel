using MassTransit;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts.Events;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Valuation;

public static class LatestValuation
{
    // Publishes nothing for a user without a snapshot; the caller's SaveChangesAsync commits the outbox row.
    public static async Task PublishAsync(
        ReportingDbContext db,
        ConsumeContext context,
        TimeProvider timeProvider,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: a consumer has no request user and reads the user the event names.
        var snapshot = await db.ValuationSnapshots
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.Date)
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot is null)
        {
            return;
        }

        // IgnoreQueryFilters: the lines belong to the event's user, not to a request user.
        var lines = await db.AssetValuations
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(l => l.UserId == userId && l.Date == snapshot.Date)
            .Select(l => new { l.AssetClass, l.ValuePln })
            .ToListAsync(cancellationToken);

        var byAssetClass = lines
            .GroupBy(l => l.AssetClass)
            .Select(g => new AssetClassValue { AssetClass = g.Key, ValuePln = g.Sum(l => l.ValuePln) })
            .Where(v => v.ValuePln != 0m)
            .OrderBy(v => v.AssetClass)
            .ToList();

        await context.Publish(
            new LatestValuationChanged
            {
                UserId = userId,
                Date = snapshot.Date,
                IsStale = snapshot.IsStale,
                TotalPln = snapshot.TotalPln,
                ByAssetClass = byAssetClass,
                OccurredAtUtc = timeProvider.GetUtcNow(),
            },
            cancellationToken);
    }
}
