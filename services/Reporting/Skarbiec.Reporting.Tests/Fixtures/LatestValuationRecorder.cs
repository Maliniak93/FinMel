using System.Collections.Concurrent;
using MassTransit;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Reporting.Tests.Fixtures;

// Collects LatestValuationChanged deliveries so a fact can assert on the publish alone.
public sealed class LatestValuationRecorder
{
    private readonly ConcurrentQueue<LatestValuationChanged> _delivered = new();

    public void Record(LatestValuationChanged message) => _delivered.Enqueue(message);

    public List<LatestValuationChanged> DeliveredFor(Guid userId) =>
        [.. _delivered.Where(message => message.UserId == userId)];

    public async Task<LatestValuationChanged> WaitForDeliveryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var message = _delivered.FirstOrDefault(m => m.UserId == userId);
            if (message is not null)
            {
                return message;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"A LatestValuationChanged for user {userId} was never delivered.");
    }

    // Nothing signals a surplus publish, so give one time to land before the caller counts.
    public async Task<List<LatestValuationChanged>> SettledDeliveriesForAsync(Guid userId, CancellationToken cancellationToken)
    {
        await WaitForDeliveryAsync(userId, cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

        return DeliveredFor(userId);
    }
}

public sealed class LatestValuationRecorderConsumer(LatestValuationRecorder recorder) : IConsumer<LatestValuationChanged>
{
    public Task Consume(ConsumeContext<LatestValuationChanged> context)
    {
        recorder.Record(context.Message);
        return Task.CompletedTask;
    }
}
