namespace Skarbiec.Portfolio.Features.Bonds.GetBondEarlyRedemptionPreview;

/// <summary>PLN totals for the redeemed bonds, exactly what an early redemption on that date would store.</summary>
public sealed record BondEarlyRedemptionPreviewResponse
{
    /// <summary>1-based index of the period the date falls in.</summary>
    public required int PeriodIndex { get; init; }

    public required int BondCount { get; init; }

    /// <summary>Bond count × the interest accrued per bond up to the date.</summary>
    public required decimal InterestDue { get; init; }

    /// <summary>Bond count × the early-redemption fee per bond, taken from the interest.</summary>
    public required decimal Fee { get; init; }

    /// <summary>Bond count × (100 − purchase price).</summary>
    public required decimal DiscountIncome { get; init; }

    /// <summary>Interest − fee + discount, floored at 0.</summary>
    public required decimal TaxableIncome { get; init; }

    /// <summary>Belka tax on the taxable income; 0 when tax-exempt.</summary>
    public required decimal Tax { get; init; }

    public required decimal Proceeds { get; init; }
}
