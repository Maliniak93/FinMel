using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetSyncStatus;

/// <summary>Reads the most recent <see cref="SyncRun"/> — including one still in progress — for the
/// T2.14 UI's "last sync" affordance.</summary>
public sealed class GetSyncStatusHandler(MarketDataDbContext dbContext)
{
    public async Task<Result<SyncStatusResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var run = await dbContext.SyncRuns
            .AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (run is null)
        {
            return new SyncStatusResponse { HasRun = false };
        }

        return new SyncStatusResponse
        {
            HasRun = true,
            RunId = run.Id,
            Status = run.Status,
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            SyncedCount = run.SyncedCount,
            NoDataCount = run.NoDataCount,
            FailedCount = run.FailedCount,
        };
    }
}
