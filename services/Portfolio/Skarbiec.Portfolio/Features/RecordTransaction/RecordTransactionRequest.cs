using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.RecordTransaction;

public sealed record RecordTransactionRequest
{
    public required TransactionType Type { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public decimal Fee { get; init; }

    public required DateOnly Date { get; init; }
}
