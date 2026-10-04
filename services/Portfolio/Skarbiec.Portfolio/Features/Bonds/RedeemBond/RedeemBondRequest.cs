namespace Skarbiec.Portfolio.Features.Bonds.RedeemBond;

/// <summary>Redeems the whole holding at maturity; everything is dated the maturity date.</summary>
public sealed record RedeemBondRequest
{
    /// <summary>The PLN Cash asset that receives the proceeds.</summary>
    public required Guid DestinationAssetId { get; init; }
}
