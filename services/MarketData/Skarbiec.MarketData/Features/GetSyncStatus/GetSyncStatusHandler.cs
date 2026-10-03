using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetSyncStatus;

public sealed class GetSyncStatusHandler(MarketDataDbContext dbContext)
{
    public async Task<Result<SyncStatusResponse>> HandleAsync(CancellationToken cancellationToken)
    {
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
