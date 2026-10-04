namespace Skarbiec.Portfolio.Features.Bonds.SettleBondInterest;

/// <summary>Periods must be the next unsettled ones, in order; the handler checks them.</summary>
public sealed record SettleBondInterestRequest
{
    public required IReadOnlyList<SettleBondPeriodRequest> Periods { get; init; }

    /// <summary>The PLN Cash asset a coupon is paid to; required for ROR, DOR and COI, rejected for capitalising types.</summary>
    public Guid? DestinationAssetId { get; init; }
}

public sealed record SettleBondPeriodRequest
{
    /// <summary>1-based.</summary>
    public required int PeriodIndex { get; init; }

    /// <summary>The MF rate of a variable period from the second on; omitted for period 1 and fixed-rate types, which use the terms.</summary>
    public decimal? RatePercent { get; init; }
}
