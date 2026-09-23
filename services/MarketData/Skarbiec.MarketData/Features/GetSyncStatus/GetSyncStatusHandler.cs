using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetSyncStatus;

/// <summary>Reads the most recent <see cref="SyncRun"/> of each kind — including one still in
/// progress — for the T2.14 UI's "last sync" affordance (spec-04 design decision 14).</summary>
public sealed class GetSyncStatusHandler(MarketDataDbContext dbContext)
{
    public async Task<Result<SyncStatusResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        // One indexed lookup per kind — (Kind, StartedAt desc).
        var prices = await LatestAsync(SyncRunKind.Prices, cancellationToken);
        var fx = await LatestAsync(SyncRunKind.Fx, cancellationToken);
        var backfill = await LatestAsync(SyncRunKind.Backfill, cancellationToken);

        return new SyncStatusResponse
        {
            HasRun = prices is not null || fx is not null || backfill is not null,
            Prices = prices,
            Fx = fx,
            Backfill = backfill,
        };
    }

    private Task<SyncRunSummary?> LatestAsync(SyncRunKind kind, CancellationToken cancellationToken) =>
        dbContext.SyncRuns
            .AsNoTracking()
            .Where(r => r.Kind == kind)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new SyncRunSummary
            {
                RunId = r.Id,
                Status = r.Status,
                StartedAt = r.StartedAt,
                FinishedAt = r.FinishedAt,
                SyncedCount = r.SyncedCount,
                NoDataCount = r.NoDataCount,
                FailedCount = r.FailedCount,
            })
            .FirstOrDefaultAsync(cancellationToken);
}
