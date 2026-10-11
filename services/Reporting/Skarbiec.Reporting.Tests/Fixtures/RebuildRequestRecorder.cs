using System.Collections.Concurrent;
using MassTransit;
using Skarbiec.Reporting.Messaging;

namespace Skarbiec.Reporting.Tests.Fixtures;

// Counts HistoryRebuildRequested deliveries without running the rebuild, so a fact can assert on the publish alone.
public sealed class RebuildRequestRecorder
{
    private readonly ConcurrentQueue<HistoryRebuildRequested> _delivered = new();

    public void Record(HistoryRebuildRequested message) => _delivered.Enqueue(message);

    public List<HistoryRebuildRequested> DeliveredFor(Guid userId) =>
        [.. _delivered.Where(message => message.UserId == userId)];

    public async Task WaitForDeliveryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_delivered.Any(message => message.UserId == userId))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"A HistoryRebuildRequested for user {userId} was never delivered.");
    }
}

public sealed class RebuildRequestRecorderConsumer(RebuildRequestRecorder recorder) : IConsumer<HistoryRebuildRequested>
{
    public Task Consume(ConsumeContext<HistoryRebuildRequested> context)
    {
        recorder.Record(context.Message);
        return Task.CompletedTask;
    }
}
