using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Bonds.UpdateBond;

public sealed record UpdateBondRequest : IValidatableObject
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    /// <summary>Three uppercase letters of the bond type plus four digits, e.g. EDO1036.</summary>
    [Required, RegularExpression(BondTermsValidation.SeriesCodePattern)]
    public required string SeriesCode { get; init; }

    public required TreasuryBondType Type { get; init; }

    public required DateOnly PurchaseDate { get; init; }

    [Range(1, 1_000_000)]
    public required int BondCount { get; init; }

    /// <summary>PLN per bond of 100 PLN nominal.</summary>
    [Range(typeof(decimal), "0", "100", MinimumIsExclusive = true)]
    public required decimal PurchasePricePerBond { get; init; }

    [Range(typeof(decimal), "0", "100")]
    public required decimal FirstPeriodRatePercent { get; init; }

    /// <summary>Null for a fixed-rate type.</summary>
    [Range(typeof(decimal), "0", "100")]
    public decimal? MarginPercent { get; init; }

    /// <summary>PLN per bond.</summary>
    [Range(typeof(decimal), "0", "100")]
    public required decimal EarlyRedemptionFeePerBond { get; init; }

    /// <summary>IKE/IKZE: no Belka tax.</summary>
    public bool TaxExempt { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        BondTermsValidation.Validate(
            validationContext,
            SeriesCode,
            Type,
            PurchaseDate,
            PurchasePricePerBond,
            FirstPeriodRatePercent,
            MarginPercent,
            EarlyRedemptionFeePerBond);
}
