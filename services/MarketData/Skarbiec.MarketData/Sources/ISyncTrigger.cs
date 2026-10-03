namespace Skarbiec.MarketData.Sources;

public enum SyncTriggerOutcome
{
    Started,
    AlreadyRunning,
}

// Runs the whole daily sync on demand and refuses a second call while the first is in flight.
public interface ISyncTrigger
{
    Task<SyncTriggerOutcome> TriggerAsync(CancellationToken cancellationToken);
}
