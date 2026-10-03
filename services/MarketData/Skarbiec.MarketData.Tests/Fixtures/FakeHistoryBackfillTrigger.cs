using System.Collections.Concurrent;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures;

public sealed class FakeHistoryBackfillTrigger : IHistoryBackfillTrigger
{
    private readonly ConcurrentBag<Guid> _enqueued = [];

    public IReadOnlyCollection<Guid> Enqueued => _enqueued;

    public int CountFor(Guid instrumentId) => _enqueued.Count(id => id == instrumentId);

    public Task EnqueueAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        _enqueued.Add(instrumentId);
        return Task.CompletedTask;
    }
}
