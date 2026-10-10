namespace Skarbiec.Contracts.Events;

public sealed record PortfolioArchived
{
    public required Guid PortfolioId { get; init; }
}
