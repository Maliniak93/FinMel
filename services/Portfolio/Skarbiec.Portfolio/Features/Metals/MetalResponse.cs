using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Metals;

public sealed record MetalResponse
{
    public required Guid AssetId { get; init; }

    public required bool IsArchived { get; init; }

    public Guid? GroupId { get; init; }

    public required string Name { get; init; }
    public required Metal Metal { get; init; }

    public required decimal FineWeightGramsPerPiece { get; init; }

    /// <summary>The asset's quantity, which transactions move.</summary>
    public required decimal Pieces { get; init; }

    /// <summary>Pieces × FineWeightGramsPerPiece.</summary>
    public required decimal TotalFineGrams { get; init; }

    /// <summary>PLN paid for the pieces held, from the cash legs of the purchases; null when a purchase has none.</summary>
    public decimal? CostPln { get; init; }
}

public static class MetalMappingExtensions
{
    public static MetalResponse ToResponse(
        this MetalHolding holding, Asset asset, decimal? costPln = null) => new()
        {
            AssetId = asset.Id,
            IsArchived = asset.IsArchived,
            GroupId = asset.GroupId,
            Name = asset.Name,
            Metal = holding.Metal,
            FineWeightGramsPerPiece = holding.FineWeightGramsPerPiece,
            Pieces = asset.Quantity,
            TotalFineGrams = MetalWeight.TotalFineGrams(asset.Quantity, holding.FineWeightGramsPerPiece),
            CostPln = costPln
        };
}
