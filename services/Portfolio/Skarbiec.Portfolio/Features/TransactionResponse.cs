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

    /// <summary>The asset's currency — the one <see cref="UnitPrice"/> is in.</summary>
    public required string Currency { get; init; }

    /// <summary>
    /// <see cref="Quantity"/> × <see cref="UnitPrice"/> in PLN at the rate frozen on the transaction's
    /// date (ADR-026), rounded to 2 places; <see langword="null"/> when no rate was known.
    /// </summary>
    public decimal? ValuePln { get; init; }

    public required DateOnly Date { get; init; }

    /// <summary>
    /// The counterpart of a transfer leg (asset-transfers-deposit-funding); <see langword="null"/> on
    /// an ordinary transaction. A leg is changed only through its transfer's entry point.
    /// </summary>
    public TransactionTransferResponse? Transfer { get; init; }
}

public static class TransactionMappingExtensions
{
    /// <param name="transaction">The stored transaction.</param>
    /// <param name="currency">The owning asset's currency.</param>
    /// <param name="transfer">The counterpart when <paramref name="transaction"/> is a transfer leg.</param>
    public static TransactionResponse ToResponse(this Transaction transaction, string currency, TransactionTransferResponse? transfer = null) => new()
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
        Transfer = transfer
    };
}
