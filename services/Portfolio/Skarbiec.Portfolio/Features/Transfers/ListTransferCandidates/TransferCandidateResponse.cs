namespace Skarbiec.Portfolio.Features.Transfers.ListTransferCandidates;

public sealed record TransferCandidateResponse
{
    public required Guid AssetId { get; init; }
    public required string Name { get; init; }
    public required Guid PortfolioId { get; init; }
    public required string PortfolioName { get; init; }
    public required decimal Balance { get; init; }
}
