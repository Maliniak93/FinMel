namespace Skarbiec.Portfolio.Features.Transfers;

/// <summary>
/// The other side of a transfer leg, as <see cref="TransactionResponse.Transfer"/> carries it
/// (asset-transfers-deposit-funding): what the transactions view labels "Transfer to/from
/// &lt;asset&gt; (&lt;portfolio&gt;)".
/// </summary>
public sealed record TransactionTransferResponse
{
    /// <summary>The <c>TransferId</c> both legs share — what <c>DELETE /transfers/{transferId}</c> takes.</summary>
    public required Guid TransferId { get; init; }

    /// <summary>
    /// A manual route (<see cref="TransferRoutes.IsManual"/>, savings-cash-transfers): the transfer is
    /// deleted through <c>/transfers</c>. <see langword="false"/>: its own slice (a deposit) owns it.
    /// </summary>
    public required bool Manual { get; init; }

    public required Guid CounterpartAssetId { get; init; }
    public required string CounterpartAssetName { get; init; }
    public required Guid CounterpartPortfolioId { get; init; }
    public required string CounterpartPortfolioName { get; init; }
    public required TransferDirection Direction { get; init; }
}
