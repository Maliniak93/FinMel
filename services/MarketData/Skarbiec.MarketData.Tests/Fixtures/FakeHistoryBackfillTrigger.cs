using System.Collections.Concurrent;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures;

/// <summary>Records every <see cref="IHistoryBackfillTrigger.EnqueueAsync"/> call instead of actually
/// scheduling a Quartz job — lets <c>InstrumentUsageConsumerTests</c> assert "enqueued exactly once"
/// (spec-04 AC11-12) without a real scheduler in play.</summary>
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
