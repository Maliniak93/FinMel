using System.Diagnostics;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Features.TriggerSync;

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
