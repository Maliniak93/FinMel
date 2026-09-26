namespace Skarbiec.Portfolio.Features.Transfers.ListTransferCandidates;

/// <summary>One asset a transfer can move money from or to, with its current balance (its quantity).</summary>
public sealed record TransferCandidateResponse
{
    public required Guid AssetId { get; init; }
    public required string Name { get; init; }
    public required Guid PortfolioId { get; init; }
    public required string PortfolioName { get; init; }
    public required decimal Balance { get; init; }
}
