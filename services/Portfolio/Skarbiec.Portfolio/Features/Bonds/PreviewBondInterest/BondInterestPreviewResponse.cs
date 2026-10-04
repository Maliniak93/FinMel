namespace Skarbiec.Portfolio.Features.Bonds.PreviewBondInterest;

public sealed record BondInterestPreviewResponse
{
    public required IReadOnlyList<BondInterestPreviewRow> Rows { get; init; }
    public required BondInterestPreviewTotals Totals { get; init; }
}

public sealed record BondInterestPreviewRow
{
    public required int PeriodIndex { get; init; }
    public required DateOnly Start { get; init; }
    public required DateOnly End { get; init; }
    public required decimal RatePercent { get; init; }
    public required int BondCount { get; init; }

    /// <summary>PLN for all the bonds of the lot.</summary>
    public required decimal Gross { get; init; }

    /// <summary>Belka tax, PLN; 0 for capitalised interest.</summary>
    public required decimal Tax { get; init; }

    /// <summary>What reaches Cash for a coupon; for capitalised interest it equals the gross credited to the bond.</summary>
    public required decimal Net { get; init; }
}

public sealed record BondInterestPreviewTotals
{
    public required decimal Gross { get; init; }
    public required decimal Tax { get; init; }
    public required decimal Net { get; init; }
}
