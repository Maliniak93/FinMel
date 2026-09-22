using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features;

public sealed record AssetResponse
{
    public required Guid Id { get; init; }
    public required Guid PortfolioId { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required AssetValuationMode ValuationMode { get; init; }
    public required string Name { get; init; }
    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? ManualValue { get; init; }
    public DateOnly? ManualValueDate { get; init; }
    public Guid? InstrumentId { get; init; }

    /// <summary>How many transactions the asset holds — counted from the Transactions table, never a stored counter (spec-02).</summary>
    public required int TransactionCount { get; init; }
}

public static class AssetMappingExtensions
{
    /// <summary>
    /// spec-02: <paramref name="transactionCount"/> is a parameter because the count lives in the
    /// Transactions table, not on the row — read slices project it as a correlated subquery inside
    /// their own query, and a slice that already knows the number passes it directly.
    /// </summary>
    public static AssetResponse ToResponse(this Asset asset, int transactionCount) => new()
    {
        Id = asset.Id,
        PortfolioId = asset.PortfolioId,
        AssetClass = asset.AssetClass,
        ValuationMode = asset.ValuationMode,
        Name = asset.Name,
        Currency = asset.Currency,
        Quantity = asset.Quantity,
        ManualValue = asset.ManualValueAmount,
        ManualValueDate = asset.ManualValueDate,
        InstrumentId = asset.InstrumentId,
        TransactionCount = transactionCount
    };
}
