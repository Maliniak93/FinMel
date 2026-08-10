using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.GetPositionsForValuation;

/// <summary>One asset, across any user, as Reporting's valuation consumer (T2.11) needs it.</summary>
public sealed record PositionForValuationResponse
{
    public required Guid UserId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required Guid AssetId { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }
    public Guid? InstrumentId { get; init; }
    public decimal? ManualValueAmount { get; init; }
    public DateOnly? ManualValueDate { get; init; }
}

public static class PositionForValuationResponseExtensions
{
    public static PositionForValuationResponse ToPositionForValuationResponse(this Asset asset) => new()
    {
        UserId = asset.UserId,
        PortfolioId = asset.PortfolioId,
        AssetId = asset.Id,
        AssetClass = asset.AssetClass,
        Currency = asset.Currency,
        Quantity = asset.Quantity,
        InstrumentId = asset.InstrumentId,
        ManualValueAmount = asset.ManualValueAmount,
        ManualValueDate = asset.ManualValueDate,
    };
}
