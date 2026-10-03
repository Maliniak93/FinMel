namespace Skarbiec.Portfolio.Features.Transfers.CreateTransfer;

public sealed record CreateTransferResponse
{
    public required Guid TransferId { get; init; }
}
