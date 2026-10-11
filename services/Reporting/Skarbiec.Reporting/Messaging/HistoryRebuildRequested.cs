namespace Skarbiec.Reporting.Messaging;

// Reporting-internal: the HistoryRebuildRequest row holds the range, the message only wakes the consumer.
public sealed record HistoryRebuildRequested
{
    public required Guid UserId { get; init; }
}
