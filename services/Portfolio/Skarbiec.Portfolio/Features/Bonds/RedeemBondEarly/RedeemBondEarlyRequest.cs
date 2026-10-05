namespace Skarbiec.Portfolio.Features.Bonds.RedeemBondEarly;

/// <summary>Redeems some or all of the holding before maturity; every write is dated the request's date.</summary>
public sealed record RedeemBondEarlyRequest
{
    public required DateOnly Date { get; init; }

    public required int BondCount { get; init; }

    /// <summary>The MF rate of the running period from the second on; omitted for period 1 and fixed-rate types, which use the terms.</summary>
    public decimal? RunningPeriodRatePercent { get; init; }

    /// <summary>The PLN Cash asset that receives the proceeds.</summary>
    public required Guid DestinationAssetId { get; init; }
}
