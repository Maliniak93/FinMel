using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.UpdateTransaction;

public sealed record UpdateTransactionRequest
{
    public required TransactionType Type { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required DateOnly Date { get; init; }

    /// <summary>The broker's fee in PLN on a stock or ETF Buy or Sell; omitted when there is none.</summary>
    public decimal? FeeAmount { get; init; }

    /// <summary>The PLN amount really paid or received, without the fee, for a stock or ETF Buy or Sell in another currency; omitted to settle at the market rate.</summary>
    public decimal? AmountPln { get; init; }
}
