using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

public sealed class MetalHolding : IUserOwned
{
    public required Guid AssetId { get; init; }
    public Guid UserId { get; set; }
    public required Metal Metal { get; set; }

    public required decimal FineWeightGramsPerPiece { get; set; }
}
