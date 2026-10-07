using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Metals;

public sealed record MetalResponse
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required string PortfolioName { get; init; }
    public required bool PortfolioIsArchived { get; init; }

    public required bool IsArchived { get; init; }

    public required string Name { get; init; }
    public required Metal Metal { get; init; }

    public required decimal FineWeightGramsPerPiece { get; init; }

    /// <summary>The asset's quantity, which transactions move.</summary>
    public required decimal Pieces { get; init; }

    /// <summary>Pieces × FineWeightGramsPerPiece.</summary>
    public required decimal TotalFineGrams { get; init; }
}

public static class MetalMappingExtensions
{
    public static MetalResponse ToResponse(
        this MetalHolding holding, Asset asset, string portfolioName, bool portfolioIsArchived) => new()
        {
            AssetId = asset.Id,
            PortfolioId = asset.PortfolioId,
            PortfolioName = portfolioName,
            PortfolioIsArchived = portfolioIsArchived,
            IsArchived = asset.IsArchived,
            Name = asset.Name,
            Metal = holding.Metal,
            FineWeightGramsPerPiece = holding.FineWeightGramsPerPiece,
            Pieces = asset.Quantity,
            TotalFineGrams = MetalWeight.TotalFineGrams(asset.Quantity, holding.FineWeightGramsPerPiece)
        };
}
