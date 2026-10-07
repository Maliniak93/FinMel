using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.RecordTransaction;

public sealed record RecordTransactionRequest
{
    public required TransactionType Type { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required DateOnly Date { get; init; }

    /// <summary>A PLN Cash account paying for a precious-metal Buy or receiving a Sell's proceeds of quantity × unit price.</summary>
    public Guid? CashAssetId { get; init; }
}
