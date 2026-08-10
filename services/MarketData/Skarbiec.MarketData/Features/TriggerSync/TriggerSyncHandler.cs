using System.Diagnostics;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Features.TriggerSync;

/// <summary>Backs the manual "sync now" button (T2.14, E4 [S]) — fires <see cref="PriceSyncJob"/> on
/// demand instead of waiting for its cron schedule.</summary>
public sealed class TriggerSyncHandler(ISyncTrigger syncTrigger)
{
    public async Task<Result> HandleAsync(CancellationToken cancellationToken)
    {
        var outcome = await syncTrigger.TriggerAsync(cancellationToken);

        return outcome switch
        {
            SyncTriggerOutcome.Started => Result.Success(),
            SyncTriggerOutcome.AlreadyRunning => SyncErrors.AlreadyRunning,
            _ => throw new UnreachableException($"Unhandled {nameof(SyncTriggerOutcome)}: {outcome}"),
        };
    }
}
