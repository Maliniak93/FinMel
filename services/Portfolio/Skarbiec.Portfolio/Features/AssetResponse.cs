using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features;

public sealed record AssetResponse
{
    public required Guid Id { get; init; }
    public required Guid PortfolioId { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required string Name { get; init; }
    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? ManualValue { get; init; }
    public DateOnly? ManualValueDate { get; init; }
    public Guid? InstrumentId { get; init; }
    public required int TransactionCount { get; init; }
}

public static class AssetMappingExtensions
{
    public static AssetResponse ToResponse(this Asset asset) => new()
    {
        Id = asset.Id,
        PortfolioId = asset.PortfolioId,
        AssetClass = asset.AssetClass,
        Name = asset.Name,
        Currency = asset.Currency,
        Quantity = asset.Quantity,
        ManualValue = asset.ManualValueAmount,
        ManualValueDate = asset.ManualValueDate,
        InstrumentId = asset.InstrumentId,
        TransactionCount = asset.TransactionCount
    };
}
