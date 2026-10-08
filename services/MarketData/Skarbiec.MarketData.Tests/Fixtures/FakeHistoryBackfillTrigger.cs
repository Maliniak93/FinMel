using System.Collections.Concurrent;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures;

public sealed class FakeHistoryBackfillTrigger : IHistoryBackfillTrigger
{
    private readonly ConcurrentQueue<(Guid InstrumentId, DateOnly From)> _enqueued = [];

    public IReadOnlyCollection<(Guid InstrumentId, DateOnly From)> Enqueued => _enqueued;

    public int CountFor(Guid instrumentId) => _enqueued.Count(e => e.InstrumentId == instrumentId);

    public IReadOnlyList<DateOnly> FromsFor(Guid instrumentId) =>
        _enqueued.Where(e => e.InstrumentId == instrumentId).Select(e => e.From).ToList();

    public Task EnqueueAsync(Guid instrumentId, DateOnly from, CancellationToken cancellationToken)
    {
        _enqueued.Enqueue((instrumentId, from));
        return Task.CompletedTask;
    }
}
