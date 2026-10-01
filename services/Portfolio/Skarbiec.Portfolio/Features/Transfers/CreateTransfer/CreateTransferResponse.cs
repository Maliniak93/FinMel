namespace Skarbiec.Portfolio.Features.Transfers.CreateTransfer;

/// <summary>The id both legs share — what <c>DELETE /transfers/{transferId}</c> takes.</summary>
public sealed record CreateTransferResponse
{
    public required Guid TransferId { get; init; }
}
