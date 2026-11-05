using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures;

public sealed class FakeFxHistoryBackfillTrigger(Exception? failure = null) : IFxHistoryBackfillTrigger
{
    private int _attempts;

    public int Attempts => Volatile.Read(ref _attempts);

    public Task EnqueueAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _attempts);
        return failure is null ? Task.CompletedTask : Task.FromException(failure);
    }
}
