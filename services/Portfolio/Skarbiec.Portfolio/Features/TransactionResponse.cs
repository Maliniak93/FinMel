using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features;

public sealed record TransactionResponse
{
    public required Guid Id { get; init; }
    public required Guid AssetId { get; init; }
    public required TransactionType Type { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public required decimal Fee { get; init; }
    public required DateOnly Date { get; init; }
}

public static class TransactionMappingExtensions
{
    public static TransactionResponse ToResponse(this Transaction transaction) => new()
    {
        Id = transaction.Id,
        AssetId = transaction.AssetId,
        Type = transaction.Type,
        Quantity = transaction.Quantity,
        UnitPrice = transaction.UnitPriceAmount,
        Fee = transaction.FeeAmount,
        Date = transaction.Date
    };
}
