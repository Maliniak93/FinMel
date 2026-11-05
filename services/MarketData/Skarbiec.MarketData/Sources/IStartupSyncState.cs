namespace Skarbiec.MarketData.Sources;

// In-process: the start sequence runs jobs directly, so no Quartz trigger or running SyncRun marks it.
public interface IStartupSyncState
{
    bool IsRunning { get; }

    IDisposable Begin();
}

public sealed class StartupSyncState : IStartupSyncState
{
    private volatile bool _running;

    public bool IsRunning => _running;

    public IDisposable Begin()
    {
        _running = true;
        return new Scope(this);
    }

    private sealed class Scope(StartupSyncState state) : IDisposable
    {
        public void Dispose() => state._running = false;
    }
}
