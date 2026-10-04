namespace Skarbiec.Portfolio.Features.Bonds.GetBondRedemptionPreview;

/// <summary>PLN totals for the whole holding, exactly what a redemption today would store.</summary>
public sealed record BondRedemptionPreviewResponse
{
    /// <summary>The maturity date, which dates every redemption write.</summary>
    public required DateOnly Date { get; init; }

    public required int BondCount { get; init; }

    /// <summary>Bond count × (C_n − 100) for a capitalising type; 0 for a coupon type, whose coupons were already paid.</summary>
    public required decimal CapitalisedInterest { get; init; }

    /// <summary>Bond count × (100 − purchase price), credited to the bond at redemption.</summary>
    public required decimal DiscountIncome { get; init; }

    public required decimal TaxableIncome { get; init; }

    /// <summary>Belka tax on the taxable income; 0 when tax-exempt.</summary>
    public required decimal Tax { get; init; }

    public required decimal Proceeds { get; init; }
}
