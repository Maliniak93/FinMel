using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.UpdateTransaction;

public sealed record UpdateTransactionRequest
{
    public required TransactionType Type { get; init; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal Quantity { get; init; }

    /// <summary>Required for everything except a precious metal, whose Buy or Sell takes TotalAmount instead.</summary>
    public decimal? UnitPrice { get; init; }

    /// <summary>The invoice total in PLN of a precious-metal Buy or Sell; the unit price is derived from it.</summary>
    public decimal? TotalAmount { get; init; }

    public required DateOnly Date { get; init; }

    /// <summary>The settlement source of a precious-metal Buy or Sell; it must be the one the trade already settles with.</summary>
    public Guid? CashAssetId { get; init; }

    /// <summary>The broker's fee in PLN on a stock or ETF Buy or Sell; omitted when there is none.</summary>
    public decimal? FeeAmount { get; init; }

    /// <summary>The PLN amount really paid or received, without the fee, for a stock or ETF Buy or Sell in another currency; omitted to settle at the market rate.</summary>
    public decimal? AmountPln { get; init; }
}
