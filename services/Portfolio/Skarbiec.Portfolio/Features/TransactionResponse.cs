using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Features;

public sealed record TransactionResponse
{
    public required Guid Id { get; init; }
    public required Guid AssetId { get; init; }
    public required TransactionType Type { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }

    /// <summary>The asset's currency, the one UnitPrice is in.</summary>
    public required string Currency { get; init; }

    /// <summary>The PLN amount entered for a stock or ETF Buy or Sell, without the fee; null when settled at the market rate.</summary>
    public decimal? AmountPln { get; init; }

    /// <summary>AmountPln when set, else Quantity × UnitPrice in PLN at the rate frozen on the transaction's date, rounded to 2 places; null when no rate was known.</summary>
    public decimal? ValuePln { get; init; }

    /// <summary>The invoice total in PLN of a precious-metal Buy or Sell, exactly the settlement leg's amount; null otherwise.</summary>
    public decimal? TotalAmount { get; init; }

    /// <summary>The broker's fee in PLN on a stock or ETF Buy or Sell; null otherwise.</summary>
    public decimal? FeeAmount { get; init; }

    public required DateOnly Date { get; init; }

    /// <summary>The counterpart of a transfer leg; null on an ordinary transaction.</summary>
    public TransactionTransferResponse? Transfer { get; init; }

    /// <summary>The last day of the settled month when this is a bank account's interest credit; null otherwise.</summary>
    public DateOnly? BankInterestPeriodEnd { get; init; }

    /// <summary>The settled period's 1-based index when this is a treasury bond's interest credit; null otherwise.</summary>
    public int? BondInterestPeriodIndex { get; init; }

    /// <summary>The current name of the recurring income when this is the deposit of its settlement; null otherwise.</summary>
    public string? RecurringIncomeName { get; init; }
}

public static class TransactionMappingExtensions
{
    public static TransactionResponse ToResponse(
        this Transaction transaction,
        string currency,
        TransactionTransferResponse? transfer = null,
        DateOnly? bankInterestPeriodEnd = null,
        int? bondInterestPeriodIndex = null,
        string? recurringIncomeName = null) => new()
        {
            Id = transaction.Id,
            AssetId = transaction.AssetId,
            Type = transaction.Type,
            Quantity = transaction.Quantity,
            UnitPrice = transaction.UnitPriceAmount,
            Currency = currency,
            AmountPln = transaction.AmountPln,
            ValuePln = transaction.AmountPln
            ?? (transaction.FxRateToPln is { } rate
                ? Math.Round(transaction.Quantity * transaction.UnitPriceAmount * rate, 2, MidpointRounding.AwayFromZero)
                : null),
            FeeAmount = transaction.FeeAmount,
            Date = transaction.Date,
            Transfer = transfer,
            BankInterestPeriodEnd = bankInterestPeriodEnd,
            BondInterestPeriodIndex = bondInterestPeriodIndex,
            RecurringIncomeName = recurringIncomeName
        };
}
