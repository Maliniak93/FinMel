namespace Skarbiec.Portfolio.Features.Transfers;

/// <summary>
/// The other side of a transfer leg, as <see cref="TransactionResponse.Transfer"/> carries it
/// (asset-transfers-deposit-funding): what the transactions view labels "Transfer to/from
/// &lt;asset&gt; (&lt;portfolio&gt;)".
/// </summary>
public sealed record TransactionTransferResponse
{
    public required Guid CounterpartAssetId { get; init; }
    public required string CounterpartAssetName { get; init; }
    public required Guid CounterpartPortfolioId { get; init; }
    public required string CounterpartPortfolioName { get; init; }
    public required TransferDirection Direction { get; init; }
}
