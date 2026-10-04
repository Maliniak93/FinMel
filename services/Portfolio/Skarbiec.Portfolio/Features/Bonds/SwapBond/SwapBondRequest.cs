using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Bonds.SwapBond;

/// <summary>Redeems the whole matured holding and buys bondCount bonds of a new series with it, dated the old maturity date.</summary>
public sealed record SwapBondRequest
{
    /// <summary>How many of the held bonds are swapped, from 1 to the holding's count; the rest join the leftover.</summary>
    public required int BondCount { get; init; }

    [Required]
    public required SwapNewBondRequest NewBond { get; init; }

    /// <summary>The PLN Cash asset that receives proceeds − bondCount × swapPricePerBond; required when that leftover is above 0.</summary>
    public Guid? DestinationAssetId { get; init; }
}

public sealed record SwapNewBondRequest : IValidatableObject
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    /// <summary>Three uppercase letters of the bond type plus four digits, e.g. EDO1036.</summary>
    [Required, RegularExpression(BondTermsValidation.SeriesCodePattern)]
    public required string SeriesCode { get; init; }

    public required TreasuryBondType Type { get; init; }

    /// <summary>The series' swap price, PLN per bond of 100 PLN nominal.</summary>
    [Range(typeof(decimal), "0", "100", MinimumIsExclusive = true)]
    public required decimal SwapPricePerBond { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public required decimal FirstPeriodRatePercent { get; init; }

    /// <summary>Null for a fixed-rate type.</summary>
    [Range(typeof(decimal), "0", "100")]
    public decimal? MarginPercent { get; init; }

    /// <summary>PLN per bond.</summary>
    [Range(typeof(decimal), "0", "100")]
    public required decimal EarlyRedemptionFeePerBond { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        BondTermsValidation.Validate(
            validationContext,
            SeriesCode,
            Type,
            purchaseDate: null,
            SwapPricePerBond,
            FirstPeriodRatePercent,
            MarginPercent,
            EarlyRedemptionFeePerBond);
}
