namespace Skarbiec.Reporting.Messaging;

// Reporting-internal: the HistoryRebuildRequest row holds the range, the message only wakes the consumer.
public sealed record PortfolioHistoryRebuildRequested
{
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
}
