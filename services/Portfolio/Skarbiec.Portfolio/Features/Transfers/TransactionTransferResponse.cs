namespace Skarbiec.Portfolio.Features.Transfers;

public sealed record TransactionTransferResponse
{
    public required Guid TransferId { get; init; }

    /// <summary>True when the transfer is deleted through /transfers; false when its own slice owns it.</summary>
    public required bool Manual { get; init; }

    public required Guid CounterpartAssetId { get; init; }
    public required string CounterpartAssetName { get; init; }
    public string? CounterpartAccountName { get; init; }
    public required TransferDirection Direction { get; init; }
}
