using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Features.TriggerSync;

public sealed class TriggerSyncHandler(ISyncTrigger syncTrigger, IStartupSyncState startupSync, MarketDataDbContext dbContext)
{
    public async Task<Result> HandleAsync(CancellationToken cancellationToken)
    {
        if (startupSync.IsRunning
            || await dbContext.SyncRuns.AnyAsync(r => r.Kind == SyncRunKind.Prices && r.Status == SyncRunStatus.Running, cancellationToken))
        {
            return SyncErrors.AlreadyRunning;
        }

        var outcome = await syncTrigger.TriggerAsync(cancellationToken);

        return outcome switch
        {
            SyncTriggerOutcome.Started => Result.Success(),
            SyncTriggerOutcome.AlreadyRunning => SyncErrors.AlreadyRunning,
            _ => throw new UnreachableException($"Unhandled {nameof(SyncTriggerOutcome)}: {outcome}"),
        };
    }
}
