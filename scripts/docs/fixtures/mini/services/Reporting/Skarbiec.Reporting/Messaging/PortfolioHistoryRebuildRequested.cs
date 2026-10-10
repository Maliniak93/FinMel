namespace Skarbiec.Reporting.Messaging;

public sealed record PortfolioHistoryRebuildRequested
{
    public required Guid PortfolioId { get; init; }
    public required Guid UserId { get; init; }
}
