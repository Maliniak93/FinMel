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

    /// <summary>Quantity × UnitPrice in PLN at the rate frozen on the transaction's date, rounded to 2 places; null when no rate was known.</summary>
    public decimal? ValuePln { get; init; }

    public required DateOnly Date { get; init; }

    /// <summary>The counterpart of a transfer leg; null on an ordinary transaction.</summary>
    public TransactionTransferResponse? Transfer { get; init; }

    /// <summary>The last day of the settled month when this is a savings account's interest credit; null otherwise.</summary>
    public DateOnly? SavingsInterestPeriodEnd { get; init; }

    /// <summary>The settled period's 1-based index when this is a treasury bond's interest credit; null otherwise.</summary>
    public int? BondInterestPeriodIndex { get; init; }
}

public static class TransactionMappingExtensions
{
    public static TransactionResponse ToResponse(
        this Transaction transaction,
        string currency,
        TransactionTransferResponse? transfer = null,
        DateOnly? savingsInterestPeriodEnd = null,
        int? bondInterestPeriodIndex = null) => new()
        {
            Id = transaction.Id,
            AssetId = transaction.AssetId,
            Type = transaction.Type,
            Quantity = transaction.Quantity,
            UnitPrice = transaction.UnitPriceAmount,
            Currency = currency,
            ValuePln = transaction.FxRateToPln is { } rate
            ? Math.Round(transaction.Quantity * transaction.UnitPriceAmount * rate, 2, MidpointRounding.AwayFromZero)
            : null,
            Date = transaction.Date,
            Transfer = transfer,
            SavingsInterestPeriodEnd = savingsInterestPeriodEnd,
            BondInterestPeriodIndex = bondInterestPeriodIndex
        };
}
