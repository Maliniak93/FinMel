using System.Collections.Concurrent;
using MassTransit;
using Skarbiec.Reporting.Messaging;

namespace Skarbiec.Reporting.Tests.Fixtures;

// Counts PortfolioHistoryRebuildRequested deliveries without running the rebuild, so a fact can assert on the publish alone.
public sealed class RebuildRequestRecorder
{
    private readonly ConcurrentQueue<PortfolioHistoryRebuildRequested> _delivered = new();

    public void Record(PortfolioHistoryRebuildRequested message) => _delivered.Enqueue(message);

    public List<PortfolioHistoryRebuildRequested> DeliveredFor(Guid portfolioId) =>
        [.. _delivered.Where(message => message.PortfolioId == portfolioId)];

    public async Task WaitForDeliveryAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_delivered.Any(message => message.PortfolioId == portfolioId))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException($"A PortfolioHistoryRebuildRequested for portfolio {portfolioId} was never delivered.");
    }
}

public sealed class RebuildRequestRecorderConsumer(RebuildRequestRecorder recorder) : IConsumer<PortfolioHistoryRebuildRequested>
{
    public Task Consume(ConsumeContext<PortfolioHistoryRebuildRequested> context)
    {
        recorder.Record(context.Message);
        return Task.CompletedTask;
    }
}
